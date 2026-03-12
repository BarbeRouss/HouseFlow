# House Flow - Architecture Technique

## Stack

| Couche | Technologie |
|--------|-------------|
| **Frontend** | Next.js 14+ (App Router, TypeScript) |
| **UI** | Tailwind CSS + Shadcn/ui |
| **Backend** | ASP.NET Core 10 Web API |
| **ORM** | Entity Framework Core |
| **Base de données** | PostgreSQL |
| **Auth** | ASP.NET Core Identity (JWT) |
| **Orchestration** | .NET Aspire |
| **Conteneurs** | Docker |

---

## Structure du projet

```
/src
  /HouseFlow.AppHost        # .NET Aspire orchestrateur
  /HouseFlow.ServiceDefaults # Configuration partagée
  /HouseFlow.Core           # Entités et interfaces
  /HouseFlow.Infrastructure # EF Core, repositories, services
  /HouseFlow.API            # Contrôleurs REST
  /HouseFlow.Frontend       # Next.js
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

### Local
```bash
dotnet run --project src/HouseFlow.AppHost
```
Lance : API (.NET) + Frontend (Next.js) + PostgreSQL (Docker)

### Production
- **Azure Container Apps** via `azd up`
- **Azure Database for PostgreSQL** (Flexible Server)

---

## Coûts estimés (MVP)

| Service | Coût |
|---------|------|
| Azure Container Apps | 0€ (tier gratuit) |
| Azure PostgreSQL | ~15€/mois |
| **Total** | **~15€/mois** |

---

**Version** : 2.0
**Date** : 2025-03-11
