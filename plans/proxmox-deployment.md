# Plan : Déploiement Proxmox Self-Hosted

## Résumé des décisions

| Question | Décision |
|----------|----------|
| Infra | VM unique Debian 12 sur Proxmox |
| Orchestration | Aspire Docker Compose Publisher |
| Reverse proxy | Traefik (géré séparément, hors projet) |
| Accès | Exposé sur internet (domaine géré par l'utilisateur) |
| CI/CD | GitHub Actions → build → GHCR → preprod auto → approval → prod |
| Registry | GitHub Container Registry (ghcr.io) |
| Versioning | CalVer (YYYY.MM.DD[-N]) |
| Environnements | Preprod (auto) + Prod (approval GitHub) |
| Backups | PostgreSQL dumps quotidiens + rétention 7j |
| Monitoring | Aspire Dashboard (premier temps) |

---

## Architecture cible

```
VM Proxmox (Docker)
│
├── preprod/
│   ├── houseflow-api      (ghcr.io/…/api:2026.03.14)
│   ├── houseflow-web      (ghcr.io/…/web:2026.03.14)
│   └── postgres-preprod   (copie de la DB prod à chaque deploy)
│
└── prod/
    ├── houseflow-api      (ghcr.io/…/api:2026.03.14)
    ├── houseflow-web      (ghcr.io/…/web:2026.03.14)
    └── postgres           (données persistantes)

Internet → Traefik (géré séparément)
              ├── app.{domaine}          → prod web     (:3000)
              ├── api.{domaine}          → prod api     (:8080)
              ├── preprod.{domaine}      → preprod web  (:3100)
              └── api-preprod.{domaine}  → preprod api  (:8180)
```

---

## Versioning — CalVer

Format : `YYYY.MM.DD` avec suffixe `-N` si plusieurs releases le même jour.

```
2026.03.14      ← première release du 14 mars
2026.03.14-2    ← deuxième release du même jour
2026.03.15      ← lendemain
```

**Tags d'images GHCR :**
```
ghcr.io/barberouss/houseflow-api:2026.03.14
ghcr.io/barberouss/houseflow-api:latest
ghcr.io/barberouss/houseflow-web:2026.03.14
ghcr.io/barberouss/houseflow-web:latest
```

Le tag CalVer est calculé automatiquement dans le CI à partir de la date + compteur.

---

## Pipeline CI/CD

### Déclencheurs

| Trigger | Build | Preprod | Prod |
|---------|-------|---------|------|
| Push sur `main` | Auto | Auto | Approval GitHub |
| `workflow_dispatch` (n'importe quelle branche) | Manuel | Manuel | Non (main uniquement) |

### Flux principal (push main)

```
Push sur main
     │
     ▼
┌─ Build ──────────────────────────────┐
│  1. aspire publish                   │
│  2. docker build (API + Frontend)    │
│  3. Tag CalVer (2026.03.14)          │
│  4. Push GHCR                        │
└──────────────┬───────────────────────┘
               │
               ▼
┌─ Deploy Preprod (auto) ─────────────────────┐
│  1. SSH dans la VM                          │
│  2. Dump DB prod → restore dans preprod     │
│  3. Pull images CalVer                      │
│  4. docker compose -f preprod up -d         │
│  5. Health check                            │
└──────────────┬──────────────────────────────┘
               │
               ▼
┌─ Approval (GitHub Environment) ─────┐
│  Reviewer approuve dans GitHub UI   │
│  (environment: production)          │
└──────────────┬──────────────────────┘
               │
               ▼
┌─ Deploy Prod ───────────────────────┐
│  1. SSH dans la VM                  │
│  2. Pull mêmes images CalVer       │
│  3. docker compose -f prod up -d   │
│  4. Health check                   │
└─────────────────────────────────────┘
```

### Flux manuel (workflow_dispatch — n'importe quelle branche)

```
Bouton "Run workflow" sur GitHub Actions
  → Choisir branche (ex: feature/new-auth)
     │
     ▼
┌─ Build ──────────────────────────────┐
│  Tag: 2026.03.14-branch-new-auth    │
│  (CalVer + suffixe branche)         │
└──────────────┬───────────────────────┘
               │
               ▼
┌─ Deploy Preprod uniquement ────────────────┐
│  Même process (sync DB + deploy)          │
│  ⛔ Pas de deploy prod (branche ≠ main)  │
└────────────────────────────────────────────┘
```

> **Note :** Le deploy prod est conditionné à `github.ref == 'refs/heads/main'`.
> Depuis une feature branch, seule la preprod est accessible.

### GitHub Environments à configurer

| Environment | Protection | Reviewers |
|-------------|-----------|-----------|
| `preprod` | Aucune (auto-deploy) | — |
| `production` | Required reviewers | Toi |

---

## Structure sur la VM

```
/opt/houseflow/
├── prod/
│   ├── docker-compose.yaml     (généré par Aspire, images :latest)
│   └── .env                    (secrets prod)
├── preprod/
│   ├── docker-compose.yaml     (variante avec ports décalés)
│   └── .env                    (secrets preprod, pointe vers DB preprod)
└── scripts/
    ├── backup.sh               (dump quotidien DB prod)
    ├── sync-db-to-preprod.sh   (copie DB prod → preprod)
    └── deploy.sh               (helper commun)
```

### Ports (internes, Traefik route par domaine)

| Service | Prod | Preprod |
|---------|------|---------|
| API | 8080 | 8180 |
| Frontend | 3000 | 3100 |
| PostgreSQL | 5432 | 5433 |

---

## Étapes d'implémentation

### 1. Package Aspire Docker Compose

Ajouter `Aspire.Hosting.Docker` au AppHost :

```csharp
// src/HouseFlow.AppHost/Program.cs
var builder = DistributedApplication.CreateBuilder(args);

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

```csharp
if (app.Environment.IsDevelopment() || app.Environment.IsProduction())
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<HouseFlowDbContext>();
    dbContext.Database.Migrate();

    if (app.Environment.IsDevelopment()) { /* seed admin existant */ }
}
```

### 4. CI/CD — GitHub Actions

**Fichier :** `.github/workflows/deploy.yml`

```yaml
name: Deploy

on:
  push:
    branches: [main]
  workflow_dispatch:  # Bouton manuel — deploy preprod depuis n'importe quelle branche

env:
  REGISTRY: ghcr.io
  API_IMAGE: ghcr.io/${{ github.repository_owner }}/houseflow-api
  WEB_IMAGE: ghcr.io/${{ github.repository_owner }}/houseflow-web

jobs:
  # ── Job 1: Build & Push ──────────────────────────
  build:
    name: Build & Push Images
    runs-on: ubuntu-latest
    timeout-minutes: 15
    permissions:
      contents: read
      packages: write
    outputs:
      version: ${{ steps.version.outputs.tag }}

    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Generate CalVer tag
        id: version
        run: |
          BASE_TAG=$(date +%Y.%m.%d)

          # Add branch suffix for non-main branches
          if [ "${{ github.ref_name }}" != "main" ]; then
            BRANCH_SLUG=$(echo "${{ github.ref_name }}" | sed 's/[^a-zA-Z0-9]/-/g' | cut -c1-20)
            BASE_TAG="${BASE_TAG}-${BRANCH_SLUG}"
          fi

          # Check if tag already exists, append counter if so
          COUNTER=1
          TAG=$BASE_TAG
          while git tag -l "$TAG" | grep -q .; do
            COUNTER=$((COUNTER + 1))
            TAG="${BASE_TAG}-${COUNTER}"
          done
          echo "tag=$TAG" >> "$GITHUB_OUTPUT"
          echo "Version: $TAG"

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
        run: dotnet run --project src/HouseFlow.AppHost -- publish

      - name: Tag & push images
        run: |
          VERSION=${{ steps.version.outputs.tag }}

          # API
          docker tag houseflow-api:latest ${{ env.API_IMAGE }}:${VERSION}
          docker tag houseflow-api:latest ${{ env.API_IMAGE }}:latest
          docker push ${{ env.API_IMAGE }}:${VERSION}
          docker push ${{ env.API_IMAGE }}:latest

          # Frontend
          docker tag houseflow-web:latest ${{ env.WEB_IMAGE }}:${VERSION}
          docker tag houseflow-web:latest ${{ env.WEB_IMAGE }}:latest
          docker push ${{ env.WEB_IMAGE }}:${VERSION}
          docker push ${{ env.WEB_IMAGE }}:latest

      - name: Create git tag
        run: |
          git tag ${{ steps.version.outputs.tag }}
          git push origin ${{ steps.version.outputs.tag }}

  # ── Job 2: Deploy Preprod (auto) ─────────────────
  deploy-preprod:
    name: Deploy Preprod
    needs: build
    runs-on: ubuntu-latest
    environment: preprod
    timeout-minutes: 10

    steps:
      - name: Deploy preprod via SSH
        uses: appleboy/ssh-action@v1
        with:
          host: ${{ secrets.DEPLOY_HOST }}
          username: ${{ secrets.DEPLOY_USER }}
          key: ${{ secrets.DEPLOY_SSH_KEY }}
          script: |
            set -e
            VERSION=${{ needs.build.outputs.version }}

            # 1. Sync prod DB to preprod
            /opt/houseflow/scripts/sync-db-to-preprod.sh

            # 2. Pull new images
            cd /opt/houseflow/preprod
            export IMAGE_TAG=${VERSION}
            docker compose pull

            # 3. Restart preprod
            docker compose up -d

            # 4. Health check
            sleep 5
            curl -f http://localhost:8180/alive || exit 1

            echo "Preprod deployed: ${VERSION}"

  # ── Job 3: Deploy Prod (manual approval, main only) ──
  deploy-prod:
    name: Deploy Production
    needs: [build, deploy-preprod]
    if: github.ref == 'refs/heads/main'
    runs-on: ubuntu-latest
    environment: production
    timeout-minutes: 10

    steps:
      - name: Deploy prod via SSH
        uses: appleboy/ssh-action@v1
        with:
          host: ${{ secrets.DEPLOY_HOST }}
          username: ${{ secrets.DEPLOY_USER }}
          key: ${{ secrets.DEPLOY_SSH_KEY }}
          script: |
            set -e
            VERSION=${{ needs.build.outputs.version }}

            # 1. Pull new images
            cd /opt/houseflow/prod
            export IMAGE_TAG=${VERSION}
            docker compose pull

            # 2. Restart prod
            docker compose up -d

            # 3. Health check
            sleep 5
            curl -f http://localhost:8080/alive || exit 1

            # 4. Cleanup old images
            docker image prune -f

            echo "Production deployed: ${VERSION}"
```

### 5. Script sync DB prod → preprod

**Fichier :** `scripts/sync-db-to-preprod.sh`

```bash
#!/bin/bash
set -e

# Copie la DB de production vers preprod
# Utilisé avant chaque déploiement preprod

PROD_COMPOSE="/opt/houseflow/prod/docker-compose.yaml"
PREPROD_COMPOSE="/opt/houseflow/preprod/docker-compose.yaml"

echo "[$(date)] Syncing prod DB to preprod..."

# 1. Dump prod
docker compose -f "$PROD_COMPOSE" exec -T postgres \
  pg_dump -U "$DB_USER" -Fc houseflow > /tmp/houseflow_prod.dump

# 2. Drop & restore in preprod
docker compose -f "$PREPROD_COMPOSE" exec -T postgres \
  dropdb -U "$DB_USER" --if-exists houseflow

docker compose -f "$PREPROD_COMPOSE" exec -T postgres \
  createdb -U "$DB_USER" houseflow

docker compose -f "$PREPROD_COMPOSE" exec -T postgres \
  pg_restore -U "$DB_USER" -d houseflow --no-owner < /tmp/houseflow_prod.dump

# 3. Cleanup
rm -f /tmp/houseflow_prod.dump

echo "[$(date)] DB sync complete."
```

### 6. Script de backup

**Fichier :** `scripts/backup.sh`

```bash
#!/bin/bash
set -e

# PostgreSQL daily backup with 7-day retention
BACKUP_DIR="/opt/houseflow/backups"
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
RETENTION_DAYS=7
PROD_COMPOSE="/opt/houseflow/prod/docker-compose.yaml"

mkdir -p "$BACKUP_DIR"

# Dump prod DB
docker compose -f "$PROD_COMPOSE" exec -T postgres \
  pg_dump -U "$DB_USER" -Fc houseflow > "$BACKUP_DIR/houseflow_$TIMESTAMP.dump"

# Compress
gzip "$BACKUP_DIR/houseflow_$TIMESTAMP.dump"

# Cleanup old backups
find "$BACKUP_DIR" -name "*.dump.gz" -mtime +$RETENTION_DAYS -delete

echo "[$(date)] Backup completed: houseflow_$TIMESTAMP.dump.gz"
```

Cron : `0 3 * * * /opt/houseflow/scripts/backup.sh >> /var/log/houseflow-backup.log 2>&1`

### 7. .env.example

**Fichier :** `.env.example`

```env
# ── Commun ──
DB_USER=houseflow
DB_PASSWORD=CHANGE_ME

# JWT (minimum 32 characters)
JWT_KEY=CHANGE_ME_MINIMUM_32_CHARS_SECRET_KEY

# GHCR
GHCR_TOKEN=ghp_xxx

# ── Prod (.env dans /opt/houseflow/prod/) ──
# JWT_ISSUER=https://api.houseflow.rouss.be
# JWT_AUDIENCE=https://houseflow.rouss.be
# CORS_ORIGINS=https://houseflow.rouss.be
# IMAGE_TAG=latest

# ── Preprod (.env dans /opt/houseflow/preprod/) ──
# JWT_ISSUER=https://api.preprod.houseflow.rouss.be
# JWT_AUDIENCE=https://preprod.houseflow.rouss.be
# CORS_ORIGINS=https://preprod.houseflow.rouss.be
# IMAGE_TAG=latest
```

### 8. Documentation setup VM

**Fichier :** `docs/deployment.md`

Guide :
1. Créer VM Debian 12 sur Proxmox (2 CPU, 4GB RAM, 40GB disk)
2. Installer Docker + Docker Compose
3. Créer structure `/opt/houseflow/{prod,preprod,scripts,backups}`
4. Configurer `.env` dans prod/ et preprod/ (secrets différents si besoin)
5. Premier déploiement : `docker compose up -d` dans prod/ puis preprod/
6. Configurer Traefik (séparément) pour router vers les bons ports
7. Configurer le cron backup
8. Configurer GitHub Environments (preprod: auto, production: required reviewer)
9. Ajouter les secrets GitHub : `DEPLOY_HOST`, `DEPLOY_USER`, `DEPLOY_SSH_KEY`

---

## Fichiers à créer/modifier

| Action | Fichier |
|--------|---------|
| Créer | `src/HouseFlow.Frontend/Dockerfile` |
| Créer | `src/HouseFlow.Frontend/.dockerignore` |
| Créer | `scripts/backup.sh` |
| Créer | `scripts/sync-db-to-preprod.sh` |
| Créer | `docs/deployment.md` |
| Créer | `.env.example` |
| Modifier | `src/HouseFlow.AppHost/Program.cs` (ajouter Docker Compose publisher) |
| Modifier | `src/HouseFlow.AppHost/HouseFlow.AppHost.csproj` (package Aspire.Hosting.Docker) |
| Modifier | `src/HouseFlow.API/Program.cs` (Production DB + CORS + migrations) |
| Modifier | `.github/workflows/deploy.yml` (pipeline complet 3 jobs) |
| Modifier | `specs/user-stories.md` (ajouter US-060) |

---

## Ordre d'exécution

1. **User Story** — Ajouter US-060 dans les specs
2. **AppHost** — Ajouter Aspire.Hosting.Docker + configurer publisher
3. **Dockerfile Frontend** — Seul Dockerfile nécessaire
4. **Adaptations Program.cs** — Production mode, CORS dynamique, migrations
5. **CI/CD** — deploy.yml avec 3 jobs (build → preprod → approval → prod)
6. **Scripts** — backup.sh + sync-db-to-preprod.sh
7. **Documentation** — Guide setup VM + .env.example
8. **Test** — `aspire publish` local pour valider la génération

---

## Migration future vers Azure

Ce setup est conçu pour être portable :
- Les images GHCR sont déjà prêtes pour Azure Container Apps
- Le compose généré par Aspire peut être remplacé par `aspire publish --publisher azure`
- Les secrets `.env` migrent vers Azure Key Vault
- La DB PostgreSQL migre vers Azure Database for PostgreSQL
- Le pipeline CalVer reste identique
