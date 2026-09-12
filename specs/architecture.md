# House Flow - Architecture Technique

## Stack

| Couche | Technologie |
|--------|-------------|
| **Frontend** | Blazor WebAssembly (.NET 10, C#) |
| **UI** | Tailwind CSS + BlazorBlueprint (icônes Lucide) |
| **Backend** | ASP.NET Core 10 Web API |
| **ORM** | Entity Framework Core |
| **Base de données** | PostgreSQL |
| **Auth** | JWT (refresh token en cookie HTTP-only) |
| **Orchestration** | .NET Aspire |
| **Conteneurs** | Docker |
| **Déploiement** | Azure Container Apps (Terraform) |

---

## Structure du projet

```
/src
  /HouseFlow.AppHost        # .NET Aspire orchestrateur
  /HouseFlow.Core           # Entités et interfaces
  /HouseFlow.Application    # Services métier, DTOs, contrats
  /HouseFlow.Infrastructure # EF Core, migrations, jobs
  /HouseFlow.API            # Contrôleurs REST
  /HouseFlow.Web            # Frontend Blazor WebAssembly (.razor)
  /HouseFlow.WebHost        # Hôte ASP.NET servant le WASM publié
```

---

## Schéma de données (MVP)

```
User
  - Id, Email, PasswordHash, FirstName, LastName, CreatedAt

House
  - Id, Name, Address, OwnerId (FK User), CreatedAt

Device
  - Id, Name, Type (enum), Brand, Model, InstallDate, HouseId (FK House)

MaintenanceType
  - Id, Name, Periodicity (enum), CustomDays, DeviceId (FK Device)

MaintenanceInstance
  - Id, Date, Cost, Provider, Notes, Status (enum), MaintenanceTypeId (FK)
```

**Enums** :
- `DeviceType` : Boiler, WoodStove, HeatPump, FireAlarm, CODetector, etc.
- `Periodicity` : Annual, Biannual, Quarterly, Monthly, Custom
- `MaintenanceStatus` : Planned, Completed, Overdue

---

## API Endpoints (MVP)

### Auth
- `POST /api/auth/register`
- `POST /api/auth/login`
- `POST /api/auth/logout`
- `GET /api/auth/me`

### Houses
- `GET /api/houses`
- `GET /api/houses/{id}`
- `POST /api/houses`
- `PUT /api/houses/{id}`
- `DELETE /api/houses/{id}`

### Devices
- `GET /api/houses/{houseId}/devices`
- `POST /api/houses/{houseId}/devices`
- `PUT /api/devices/{id}`
- `DELETE /api/devices/{id}`

### Maintenance
- `GET /api/devices/{deviceId}/maintenance-types`
- `POST /api/devices/{deviceId}/maintenance-types`
- `GET /api/maintenance-types/{id}/instances`
- `POST /api/maintenance-types/{id}/instances`
- `PUT /api/maintenance-instances/{id}`
- `DELETE /api/maintenance-instances/{id}`

---

## Déploiement

### Local (développement)
```bash
dotnet run --project src/HouseFlow.AppHost
```
Lance : API (.NET) + Frontend (Blazor WASM) + PostgreSQL (Docker), orchestrés par .NET Aspire.

### Production & Preprod — Azure Container Apps

**Infrastructure :** Terraform (`infrastructure/terraform/`)

```
Resource Group: rg-houseflow
├── Container Apps Environment: cae-houseflow
│   ├── ca-api-prod        (port 8080, /alive health check)
│   ├── ca-frontend-prod   (port 3000)
│   ├── ca-api-preprod
│   ├── ca-frontend-preprod
│   └── ca-api-pr-XX / ca-frontend-pr-XX  (éphémères par PR)
├── PostgreSQL Flexible Server: psql-houseflow (B1ms)
│   ├── houseflow_prod
│   ├── houseflow_preprod
│   └── houseflow_pr_XX   (éphémères par PR)
├── Log Analytics Workspace: law-houseflow
└── Storage Account: sthouseflowtfstate (Terraform state)
```

**Authentification CI/CD :**
- GitHub Actions → Azure : Workload Identity Federation (OIDC, pas de secret)
- Azure → GHCR : PAT fine-grained `read:packages`

**Workflows GitHub Actions :**
- `deploy.yml` : Build → GHCR push → Terraform apply (preprod auto, prod avec approval)
- `pr-preview.yml` : Env éphémère par PR (deploy on open, destroy on close, max 3)
- `pr.yml` : Tests backend + frontend + E2E

**Protections :**
- Azure Policy : allowlist de types de ressources + SKU PostgreSQL restreints
- RBAC : rôle custom "HouseFlow Deployer" (pas Contributor)
- Resource lock `CanNotDelete` sur le Resource Group
- `prevent_destroy` Terraform sur les ressources prod critiques

---

## Coûts estimés (MVP)

| Service | Coût |
|---------|------|
| Azure Container Apps | 0€ (tier gratuit) |
| Azure PostgreSQL (B1ms) | ~15€/mois |
| Log Analytics | ~2€/mois |
| **Total** | **~17€/mois** |

---

**Version** : 3.0
**Date** : 2026-03-26
