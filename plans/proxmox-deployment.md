# Plan : Déploiement Proxmox Self-Hosted

## Résumé des décisions

| Question | Décision |
|----------|----------|
| Infra | VM unique Debian 12 sur Proxmox |
| Orchestration | Aspire Docker Compose Publisher |
| Reverse proxy | Traefik (géré séparément, hors projet) |
| Accès | Exposé sur internet (domaine géré par l'utilisateur) |
| CI/CD | GitHub Actions → build images → push GHCR → SSH deploy |
| Registry | GitHub Container Registry (ghcr.io) |
| Backups | PostgreSQL dumps quotidiens + rétention 7j |
| Monitoring | Aspire Dashboard (premier temps) |

---

## Architecture cible

```
Internet → Traefik (géré séparément)
              ├── app.{domaine}  → Frontend (:3000)
              └── api.{domaine}  → API (:8080)

VM Proxmox (Docker)
└── Docker Compose (généré par Aspire)
    ├── houseflow-api     (image depuis ghcr.io)
    ├── houseflow-web     (image depuis ghcr.io)
    └── postgres          (image officielle)
```

---

## Pipeline CI/CD

```
Push sur main
     │
     ▼
GitHub Actions
     ├── 1. aspire publish → génère docker-compose.yaml + .env
     ├── 2. docker build   → build images API + Frontend
     ├── 3. docker push    → push vers ghcr.io/barberouss/houseflow-*
     └── 4. SSH deploy     → pull images + docker compose up -d
```

### Artefacts

| Artefact | Stockage | Format |
|----------|----------|--------|
| Image API | `ghcr.io/barberouss/houseflow-api:latest` | Container image (.NET 10, buildé par SDK) |
| Image Frontend | `ghcr.io/barberouss/houseflow-web:latest` | Container image (Node 22 Alpine) |
| docker-compose.yaml | Généré par `aspire publish` dans le repo | YAML paramétrisé |
| .env | Sur la VM uniquement (secrets) | Variables d'environnement |

---

## Étapes d'implémentation

### 1. Package Aspire Docker Compose

Ajouter `Aspire.Hosting.Docker` au AppHost et configurer le publisher :

```csharp
// src/HouseFlow.AppHost/Program.cs
var builder = DistributedApplication.CreateBuilder(args);

// Ajouter l'environnement Docker Compose pour le publish
builder.AddDockerComposeEnvironment("houseflow");

var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin()
    .WithDataVolume();

var houseflowDb = postgres.AddDatabase("houseflow");

var api = builder.AddProject("api", "../HouseFlow.API/HouseFlow.API.csproj")
    .WithReference(houseflowDb)
    .WaitFor(houseflowDb)
    .WithHttpEndpoint(port: 5203, env: "PORT")
    .WithExternalHttpEndpoints();

var frontend = builder.AddNpmApp("frontend", "../HouseFlow.Frontend", "dev")
    .WithReference(api)
    .WaitFor(api)
    .WithHttpEndpoint(port: 3000, env: "PORT")
    .WithExternalHttpEndpoints()
    .PublishAsDockerFile();

builder.Build().Run();
```

### 2. Dockerfile Frontend (seul Dockerfile nécessaire)

**Fichier :** `src/HouseFlow.Frontend/Dockerfile`

L'API n'a pas besoin de Dockerfile — le SDK .NET build l'image container nativement via `aspire publish`.

```dockerfile
FROM node:22-alpine AS deps
WORKDIR /app
COPY package*.json ./
RUN npm ci --only=production

FROM node:22-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
ENV NEXT_TELEMETRY_DISABLED=1
RUN npm run build

FROM node:22-alpine AS runtime
WORKDIR /app
ENV NODE_ENV=production
COPY --from=deps /app/node_modules ./node_modules
COPY --from=build /app/.next ./.next
COPY --from=build /app/public ./public
COPY --from=build /app/package.json ./
COPY --from=build /app/next.config.ts ./
COPY --from=build /app/src/messages ./src/messages
EXPOSE 3000
CMD ["npm", "start"]
```

### 3. Adaptations du code existant

#### 3a. API Program.cs — Support Production sans Aspire orchestrator

Le `else` block actuel utilise `builder.AddNpgsqlDbContext` (Aspire runtime). En production Docker, Aspire a généré le compose mais ne tourne pas comme orchestrateur. Il faut un chemin "Production" avec connection string standard.

```csharp
else if (builder.Environment.IsProduction())
{
    var connectionString = builder.Configuration.GetConnectionString("houseflow")
        ?? throw new InvalidOperationException("ConnectionStrings:houseflow not configured");
    builder.Services.AddDbContext<HouseFlowDbContext>(options =>
        options.UseNpgsql(connectionString, npgsqlOptions =>
            npgsqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));
}
else
{
    // Development: Aspire-managed
    builder.AddNpgsqlDbContext<HouseFlowDbContext>("houseflow", ...);
}
```

#### 3b. API Program.cs — CORS dynamique

Remplacer les origines CORS hardcodées par une variable d'environnement :

```csharp
var corsOrigins = Environment.GetEnvironmentVariable("CORS__Origins")?.Split(',')
    ?? new[] { "http://localhost:3000", "https://localhost:3000" };

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(corsOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});
```

#### 3c. API Program.cs — Migrations automatiques en Production

Safe pour une app single-instance. Sans le seed admin (Development only).

```csharp
if (app.Environment.IsDevelopment() || app.Environment.IsProduction())
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<HouseFlowDbContext>();
    dbContext.Database.Migrate();

    // Seed admin uniquement en Development
    if (app.Environment.IsDevelopment()) { /* seed existant */ }
}
```

### 4. CI/CD — GitHub Actions

**Fichier :** `.github/workflows/deploy.yml`

```yaml
name: Deploy

on:
  push:
    branches: [main]

jobs:
  build-and-deploy:
    name: Build, Push & Deploy
    runs-on: ubuntu-latest
    timeout-minutes: 15
    permissions:
      contents: read
      packages: write

    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Install Aspire workload
        run: dotnet workload install aspire

      - name: Login to GHCR
        uses: docker/login-action@v3
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}

      - name: Publish Aspire app
        run: |
          dotnet run --project src/HouseFlow.AppHost -- publish

      - name: Tag & push images to GHCR
        run: |
          docker tag houseflow-api:latest ghcr.io/${{ github.repository_owner }}/houseflow-api:latest
          docker tag houseflow-web:latest ghcr.io/${{ github.repository_owner }}/houseflow-web:latest
          docker push ghcr.io/${{ github.repository_owner }}/houseflow-api:latest
          docker push ghcr.io/${{ github.repository_owner }}/houseflow-web:latest

      - name: Deploy via SSH
        uses: appleboy/ssh-action@v1
        with:
          host: ${{ secrets.DEPLOY_HOST }}
          username: ${{ secrets.DEPLOY_USER }}
          key: ${{ secrets.DEPLOY_SSH_KEY }}
          script: |
            cd /opt/houseflow

            # Login GHCR
            echo "${{ secrets.GHCR_TOKEN }}" | docker login ghcr.io -u ${{ github.actor }} --password-stdin

            # Pull latest images
            docker compose pull

            # Restart with new images
            docker compose up -d

            # Cleanup old images
            docker image prune -f
```

### 5. Docker Compose sur la VM

Le fichier `docker-compose.yaml` est généré par `aspire publish` et commité dans le repo. Sur la VM, un `.env` local contient les secrets :

```env
# /opt/houseflow/.env (sur la VM uniquement, jamais commité)
DB_USER=houseflow
DB_PASSWORD=<strong-password>
JWT_KEY=<minimum-32-chars-secret>
JWT_ISSUER=https://api.example.com
JWT_AUDIENCE=https://app.example.com
CORS_ORIGINS=https://app.example.com
```

Le compose généré par Aspire référence les images GHCR et utilise les variables du `.env`.

### 6. Script de backup

**Fichier :** `scripts/backup.sh`

```bash
#!/bin/bash
# PostgreSQL daily backup with 7-day retention
BACKUP_DIR="/opt/houseflow/backups"
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
RETENTION_DAYS=7

mkdir -p "$BACKUP_DIR"

# Dump via docker compose
docker compose -f /opt/houseflow/docker-compose.yaml exec -T postgres \
  pg_dump -U "$DB_USER" houseflow | gzip > "$BACKUP_DIR/houseflow_$TIMESTAMP.sql.gz"

# Cleanup old backups
find "$BACKUP_DIR" -name "*.sql.gz" -mtime +$RETENTION_DAYS -delete

echo "[$(date)] Backup completed: houseflow_$TIMESTAMP.sql.gz"
```

Cron : `0 3 * * * /opt/houseflow/scripts/backup.sh >> /var/log/houseflow-backup.log 2>&1`

### 7. .env.example

**Fichier :** `.env.example` (commité, pour documenter les variables nécessaires)

```env
# Database
DB_USER=houseflow
DB_PASSWORD=CHANGE_ME

# JWT (minimum 32 characters)
JWT_KEY=CHANGE_ME_MINIMUM_32_CHARS_SECRET_KEY
JWT_ISSUER=https://api.yourdomain.com
JWT_AUDIENCE=https://app.yourdomain.com

# CORS
CORS_ORIGINS=https://app.yourdomain.com

# GHCR (pour docker compose pull)
GHCR_TOKEN=ghp_xxx
```

### 8. Documentation setup VM

**Fichier :** `docs/deployment.md`

Guide minimal :
1. Créer VM Debian 12 sur Proxmox (2 CPU, 4GB RAM, 40GB disk)
2. Installer Docker + Docker Compose
3. Cloner le repo dans `/opt/houseflow`
4. Copier `.env.example` → `.env` et configurer les secrets
5. `docker compose up -d`
6. Configurer Traefik (séparément) pour router vers les ports exposés
7. Configurer le cron backup
8. Ajouter les secrets GitHub pour le CI/CD

**Secrets GitHub nécessaires :**
- `DEPLOY_HOST` : IP publique ou DDNS de la VM
- `DEPLOY_USER` : utilisateur SSH sur la VM
- `DEPLOY_SSH_KEY` : clé privée SSH
- `GHCR_TOKEN` : token pour pull les images sur la VM

---

## Fichiers à créer/modifier

| Action | Fichier |
|--------|---------|
| Créer | `src/HouseFlow.Frontend/Dockerfile` |
| Créer | `src/HouseFlow.Frontend/.dockerignore` |
| Créer | `scripts/backup.sh` |
| Créer | `docs/deployment.md` |
| Créer | `.env.example` |
| Modifier | `src/HouseFlow.AppHost/Program.cs` (ajouter Docker Compose publisher) |
| Modifier | `src/HouseFlow.AppHost/HouseFlow.AppHost.csproj` (package Aspire.Hosting.Docker) |
| Modifier | `src/HouseFlow.API/Program.cs` (Production DB + CORS + migrations) |
| Modifier | `.github/workflows/deploy.yml` (pipeline complet) |
| Modifier | `specs/user-stories.md` (ajouter US-060) |

---

## Ordre d'exécution

1. **User Story** — Ajouter US-060 dans les specs
2. **AppHost** — Ajouter Aspire.Hosting.Docker + configurer publisher
3. **Dockerfile Frontend** — Seul Dockerfile nécessaire
4. **Adaptations Program.cs** — Production mode, CORS dynamique, migrations
5. **CI/CD** — deploy.yml avec build → GHCR → SSH deploy
6. **Backups** — Script + doc cron
7. **Documentation** — Guide setup VM + .env.example
8. **Test** — `aspire publish` local pour valider la génération

---

## Migration future vers Azure

Ce setup est conçu pour être portable :
- Les images GHCR sont déjà prêtes pour Azure Container Apps
- Le compose généré par Aspire peut être remplacé par un publish Azure (`aspire publish --publisher azure`)
- Les secrets `.env` migrent vers Azure Key Vault
- La DB PostgreSQL migre vers Azure Database for PostgreSQL
