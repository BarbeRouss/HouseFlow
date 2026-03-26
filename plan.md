# Plan d'implémentation — US-062 & US-063

## Vue d'ensemble

Migrer le déploiement HouseFlow de VM/SSH vers Azure Container Apps avec Terraform,
puis ajouter les environnements éphémères par PR.

**Ordre d'exécution :** US-062 d'abord (infra de base), puis US-063 (envs éphémères).

---

## Phase 1 : US-062 — Infrastructure Terraform + Workflow Azure

### Étape 1 : Structure Terraform de base

Créer `infrastructure/terraform/` avec :

```
infrastructure/terraform/
├── main.tf              # Provider azurerm, backend config
├── variables.tf         # Variables d'entrée (env, ghcr_pat, jwt_key, etc.)
├── outputs.tf           # URLs des Container Apps, FQDN PostgreSQL
├── resource-group.tf    # Resource group + management lock
├── log-analytics.tf     # Log Analytics workspace
├── container-env.tf     # Container Apps Environment
├── postgresql.tf        # PostgreSQL Flexible Server + databases + firewall
├── container-app-api.tf # Container App API (prod + preprod)
├── container-app-web.tf # Container App Frontend (prod + preprod)
└── terraform.tfvars.example  # Exemple de variables
```

**Détails clés :**
- Provider `azurerm ~> 4.0`, backend `azurerm` (Storage Account)
- `prevent_destroy` sur PostgreSQL et Container Apps prod
- Management lock `CanNotDelete` sur le RG
- Un seul PostgreSQL Flexible Server (B1ms), databases `houseflow_prod` + `houseflow_preprod`
- Firewall PostgreSQL : autoriser uniquement l'IP outbound du Container Apps Environment
- Registry credential GHCR via `var.ghcr_pat`
- Variables d'env et secrets par Container App :
  - `ConnectionStrings__houseflow` (connection string PostgreSQL)
  - `JWT__KEY`, `Jwt__Issuer`, `Jwt__Audience`
  - `CORS__ORIGINS`
  - `API_PUBLIC_URL` / `NEXT_PUBLIC_API_URL`
- Health probes : `/alive` sur l'API (port 8080), `/` sur le frontend (port 3000)
- `max_replicas = 1` pour le moment (pas d'auto-scaling)
- Chaque env (prod/preprod) = une paire de Container Apps distinctes

### Étape 2 : Workflow GitHub Actions — Deploy Azure

Remplacer `.github/workflows/deploy.yml` :

```yaml
name: Deploy

on:
  push:
    branches: [main]
  workflow_dispatch:

permissions:
  id-token: write    # OIDC
  contents: write    # Git tags
  packages: write    # GHCR push

jobs:
  build:
    # Identique à l'actuel — CalVer tag, build & push GHCR
    # On garde la logique existante

  deploy-preprod:
    needs: build
    environment: preprod
    steps:
      - az login (OIDC via azure/login)
      - terraform init (backend azurerm)
      - terraform plan -var="env=preprod" -var="image_tag=$VERSION"
      - terraform apply
      - Health check curl sur l'URL Container App

  deploy-prod:
    needs: deploy-preprod
    if: github.ref == 'refs/heads/main'
    environment: production  # Requires manual approval
    steps:
      - az login (OIDC)
      - terraform plan -var="env=prod" -var="image_tag=$VERSION"
      - terraform apply
      - Health check
```

**Points importants :**
- `azure/login` avec `client-id`, `tenant-id`, `subscription-id` (OIDC, pas de secret)
- `hashicorp/setup-terraform` pour installer Terraform
- Le job `build` reste quasi identique (CalVer + GHCR push)
- Supprimer toute référence SSH/appleboy
- Les migrations s'exécutent automatiquement au démarrage de l'API (comportement actuel dans Program.cs ligne 250)
  → Pas besoin d'un job de migration séparé pour le MVP. L'API applique `database update` au boot.

### Étape 3 : Nettoyage

- Supprimer les blocs SSH de `deploy.yml` (tout remplacer)
- Garder `infrastructure/docker-compose.prod.yaml` et `preprod.yaml` (encore utiles en local)
- Garder `infrastructure/setup-vm.sh` (archivage, pas de suppression pour l'instant)
- Adapter `sync-db.yml` : remplacer SSH par `az postgres` commands (ou le désactiver temporairement)

### Étape 4 : Mise à jour documentation

- `specs/architecture.md` : mettre à jour la section déploiement
- `PROJECT_KNOWLEDGE.md` : ajouter section Azure Container Apps

---

## Phase 2 : US-063 — Environnements éphémères par PR

### Étape 5 : Module Terraform éphémère

Créer `infrastructure/terraform/modules/ephemeral-env/` :

```
infrastructure/terraform/modules/ephemeral-env/
├── main.tf        # Container Apps API + Frontend pour un env
├── variables.tf   # pr_number, image_tag, db_connection, etc.
└── outputs.tf     # URLs des Container Apps
```

Ce module crée :
- `ca-api-pr-{N}` + `ca-frontend-pr-{N}` dans le même Container Apps Environment
- Une database `houseflow_pr_{N}` sur le PostgreSQL existant
- Les secrets/env vars nécessaires

### Étape 6 : Workflow GitHub Actions — PR Preview

Créer `.github/workflows/pr-preview.yml` :

```yaml
name: PR Preview Environment

on:
  pull_request:
    types: [opened, synchronize, reopened, closed]

jobs:
  deploy-preview:
    if: github.event.action != 'closed'
    steps:
      - Build & push images :pr-{N}
      - az login (OIDC)
      - terraform apply -var="pr_number=$PR" (module ephemeral)
      - gh pr comment avec l'URL de preview

  cleanup-preview:
    if: github.event.action == 'closed'
    steps:
      - az login (OIDC)
      - terraform destroy -var="pr_number=$PR"
      - Drop database houseflow_pr_{N}
```

**Protections :**
- Max 3 envs éphémères simultanés (vérification dans le workflow)
- TTL max configurable (cleanup cron en cas de PR orpheline)

### Étape 7 : Docker Compose local pour tests

Créer `docker-compose.test.yml` à la racine :

```yaml
services:
  db:
    image: postgres:16-alpine
    # ... avec seed data

  api:
    build:
      context: .
      dockerfile: src/HouseFlow.API/Dockerfile
    depends_on: [db]
    environment:
      ConnectionStrings__houseflow: "Host=db;..."
      # ... autres env vars

  frontend:
    build:
      context: src/HouseFlow.Frontend
      dockerfile: Dockerfile
    depends_on: [api]
    environment:
      NEXT_PUBLIC_API_URL: http://api:8080
```

---

## Fichiers créés/modifiés

### Nouveaux fichiers
- `infrastructure/terraform/main.tf`
- `infrastructure/terraform/variables.tf`
- `infrastructure/terraform/outputs.tf`
- `infrastructure/terraform/resource-group.tf`
- `infrastructure/terraform/log-analytics.tf`
- `infrastructure/terraform/container-env.tf`
- `infrastructure/terraform/postgresql.tf`
- `infrastructure/terraform/container-app-api.tf`
- `infrastructure/terraform/container-app-web.tf`
- `infrastructure/terraform/terraform.tfvars.example`
- `infrastructure/terraform/modules/ephemeral-env/main.tf`
- `infrastructure/terraform/modules/ephemeral-env/variables.tf`
- `infrastructure/terraform/modules/ephemeral-env/outputs.tf`
- `.github/workflows/pr-preview.yml`
- `docker-compose.test.yml`

### Fichiers modifiés
- `.github/workflows/deploy.yml` → réécriture complète (SSH → Azure)
- `specs/architecture.md` → section déploiement mise à jour
- `PROJECT_KNOWLEDGE.md` → section Azure ajoutée

### Fichiers conservés (pas supprimés)
- `infrastructure/docker-compose.prod.yaml` (utile comme référence)
- `infrastructure/docker-compose.preprod.yaml`
- `infrastructure/setup-vm.sh`

---

## Ordre d'implémentation

1. Terraform base (étapes 1) — infra prod/preprod
2. Workflow deploy.yml rewrite (étape 2) — CI/CD Azure
3. Nettoyage + docs (étapes 3-4)
4. Module éphémère + workflow PR (étapes 5-6)
5. Docker Compose test (étape 7)

## Notes techniques

- Les migrations DB s'exécutent au démarrage de l'API (Program.cs ligne 250) → pas de job séparé nécessaire
- Le CalVer tagging existant est conservé tel quel
- L'API écoute sur port 8080, le frontend sur 3000 (confirmé dans les Dockerfiles)
- Health endpoints : `/alive` (léger) et `/health` (avec DB check)
- Connection string key : `ConnectionStrings:houseflow` (pas `DefaultConnection`)
- Domain actuel : `houseflow.rouss.be` / `api.houseflow.rouss.be`
