# HouseFlow

Application de suivi de maintenance pour la maison. Backend .NET 10 + Frontend Next.js 15, orchestré par Aspire.

## Quick Start

```bash
# Prérequis: .NET 10 SDK, Node.js 20+

# 1. Installation
dotnet restore
cd src/HouseFlow.Frontend && npm install && cd ../..

# 2. Lancement (PostgreSQL + API + Frontend)
dotnet run --project src/HouseFlow.AppHost
```

**Accès:**
| Service | URL |
|---------|-----|
| Frontend | http://localhost:3000 |
| API | http://localhost:5203 |
| Swagger | http://localhost:5203/swagger |
| Aspire Dashboard | http://localhost:15000 |

**Utilisateur par défaut** (dev uniquement):
- Email: `admin@admin.com`
- Password: `admin`

**Alternative : Dev Container** — pour développer sans installer .NET/Node localement et éviter les conflits de ports entre sessions parallèles, voir [`.devcontainer/README.md`](.devcontainer/README.md).

## Commandes essentielles

```bash
# Tests backend (85 tests)
dotnet test

# Tests E2E frontend (70 tests)
cd src/HouseFlow.Frontend && npm test

# Générer client API TypeScript
cd src/HouseFlow.Frontend && npm run generate-client

# Créer une migration
dotnet ef migrations add <Name> --project src/HouseFlow.Infrastructure --startup-project src/HouseFlow.API
```

## Dépannage

### Port 22222 occupé (Aspire)
```bash
# Windows
netstat -ano | findstr :22222
taskkill /PID <PID> /F

# Linux/Mac
lsof -ti:22222 | xargs kill -9
```

### Erreur de migration
```bash
dotnet ef migrations add FixMigration --project src/HouseFlow.Infrastructure --startup-project src/HouseFlow.API
```

### Build frontend échoue
```bash
cd src/HouseFlow.Frontend
rm -rf node_modules .next
npm install
```

## Documentation

Pour la documentation complète (architecture, API-First workflow, database schema, guidelines):

**[PROJECT_KNOWLEDGE.md](./PROJECT_KNOWLEDGE.md)**

## Structure

```
src/
├── HouseFlow.Core/           # Entités domaine
├── HouseFlow.Application/    # DTOs, interfaces
├── HouseFlow.Infrastructure/ # EF Core, services
├── HouseFlow.API/            # Controllers REST
├── HouseFlow.AppHost/        # Orchestration Aspire
└── HouseFlow.Frontend/       # Next.js 15

tests/
├── HouseFlow.UnitTests/
└── HouseFlow.IntegrationTests/
```
