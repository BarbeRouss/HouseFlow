# HouseFlow - Project Knowledge Base

**Last Updated**: 2026-03-31

## Project Overview

**HouseFlow** is a home maintenance tracking application that helps users manage multiple properties, devices, and maintenance schedules. Built with .NET 10 backend and Next.js 15 frontend.

## Technology Stack

### Backend
- **.NET 10** with C# 13
- **ASP.NET Core Web API**
- **Entity Framework Core 10** with PostgreSQL
- **Aspire 13.1.0** for orchestration and observability
- **NSwag** for OpenAPI/Swagger documentation and backend code generation from spec
- **JWT** for authentication
- **BCrypt.Net** for password hashing
- **Onion Architecture** (Clean Architecture)

### Frontend
- **Next.js 15.5.9** with App Router
- **React 19**
- **TypeScript** (strict mode)
- **Tailwind CSS v3.4.19**
- **Shadcn/ui** components
- **TanStack Query** for data fetching
- **Zustand** for state management
- **next-intl** for internationalization (French/English)
- **Playwright 1.57.0** for E2E testing (70 tests)

### Infrastructure
- **PostgreSQL 16** for database
- **Docker** for containerization
- **Terraform** for Infrastructure as Code (`infrastructure/terraform/`)
  - `main/` — shared infra (VNet, PostgreSQL, CAE, identity, bastion)
  - `deploy-prod/` — prod Container Apps
  - `deploy-preprod/` — preprod Container Apps
  - `ephemeral/` — PR preview environments
- **Azure Container Apps** for hosting (prod, preprod, ephemeral PR envs)
- **Azure Database for PostgreSQL Flexible Server** (B1ms, shared across envs, VNet-integrated)
- **Azure VNet** (10.0.0.0/16) with delegated subnets for Container Apps (/23) and PostgreSQL (/28)
- **Entra ID (Azure AD)** passwordless auth for PostgreSQL (managed identity + periodic token refresh)
- **User-Assigned Managed Identity** shared across Container Apps for DB access
- **GitHub Actions** with OIDC Workload Identity Federation (no Azure secrets in GitHub)
- **GHCR** for container images (PAT `read:packages` for Azure pull)
- **Bastion Container App** (SSH tunnel, scale-to-zero) for private DB access via DBeaver

## Architecture

### Onion Architecture Layers

```
src/
├── HouseFlow.Core/              # Domain entities and interfaces
│   ├── Entities/                # House, Device, User, etc.
│   └── Enums/                   # HouseRole, InvitationStatus
├── HouseFlow.Application/       # Business logic and DTOs
│   ├── DTOs/                    # Data Transfer Objects
│   └── Services/                # Business services
├── HouseFlow.Infrastructure/    # Data access and external services
│   ├── Persistence/             # EF Core DbContext
│   ├── Repositories/            # Repository implementations
│   └── Services/                # AuthService, HouseService, etc.
├── HouseFlow.API/              # REST API controllers
│   └── Controllers/             # API endpoints
├── HouseFlow.AppHost/          # Aspire orchestration
└── HouseFlow.Frontend/         # Next.js application
```

### API-First Development Workflow

**CRITICAL**: This project follows an **API-First (Contract-First)** approach:

1. **Update OpenAPI Spec** (`specs/openapi.yaml`)
2. **Regenerate Frontend Client**:
   ```bash
   cd src/HouseFlow.Frontend
   npm run generate-client
   ```
3. **Regenerate Backend Code** from spec:
   ```bash
   ./scripts/generate-api.sh
   ```
   This generates:
   - **DTOs** in `Application/Generated/Contracts.g.cs` (namespace `HouseFlow.Contracts`)
   - **Controller bases** in `API/Generated/Controllers.g.cs` (namespace `HouseFlow.API.Generated`)

   Type aliases in `ContractAliases.cs` map old DTO names to generated types:
   - `RegisterRequestDto` → `HouseFlow.Contracts.RegisterRequest`
   - `LoginRequestDto` → `HouseFlow.Contracts.LoginRequest`
   - `CreateHouseRequestDto` → `HouseFlow.Contracts.CreateHouseRequest`
   - `UpdateHouseRequestDto` → `HouseFlow.Contracts.UpdateHouseRequest`
   - `CreateDeviceRequestDto` → `HouseFlow.Contracts.CreateDeviceRequest`
   - `UpdateDeviceRequestDto` → `HouseFlow.Contracts.UpdateDeviceRequest`
   - `LogMaintenanceRequestDto` → `HouseFlow.Contracts.LogMaintenanceRequest`

   DTOs not yet in the spec (Members, UserSettings, etc.) remain manual in `Application/DTOs/`.

4. **Update remaining Backend Code** if needed:
   - Manual DTOs in `Application/DTOs/` (for types not in spec)
   - Entities in `Core/Entities/`
   - Services in `Infrastructure/Services/`
5. **Run Tests** to verify everything works

**Source of Truth**: `specs/openapi.yaml`

#### Backend Code Generation (NSwag)

- **Tool**: NSwag v14.6.3 (dotnet local tool)
- **Configs**: `nswag-dtos.json` (DTOs), `nswag-controllers.json` (controller bases)
- **MSBuild integration**: Auto-regenerates when `specs/openapi.yaml` changes during build
- **Script**: `./scripts/generate-api.sh` for manual regeneration
- Generated files are committed to the repo (not build-only)

## Key Features Implemented

### Authentication & Onboarding
- User registration with email/password
- JWT-based authentication
- Auto-creates first house named "Ma Maison" on registration
- Redirects to device creation page after registration
- Single house auto-redirect: users with only 1 house are automatically redirected to it

### House Management
- Create, view, and manage houses
- Optional address fields (address, zipCode, city)
- Invite members (Owner, Collaborator, Tenant roles)
- View house details with member list

### Device Management
- Add devices to houses
- Device types: Chaudière Gaz, Pompe à Chaleur, etc.
- Optional install date
- View device details

### Maintenance Tracking
- Define maintenance types (Annual, Semestrial, etc.)
- Log maintenance instances
- View maintenance history

## Database Schema

### Key Entities

**User**
- Id (Guid)
- Email (unique)
- FirstName
- LastName
- PasswordHash
- CreatedAt
- UpdatedAt

**House** (direct user ownership, no Organization layer)
- Id (Guid)
- Name (required)
- Address (optional)
- ZipCode (optional)
- City (optional)
- UserId → User (owner)
- CreatedAt
- UpdatedAt

**Device**
- Id (Guid)
- Name
- Type
- Brand (optional)
- Model (optional)
- InstallDate (optional)
- HouseId → House
- CreatedAt
- UpdatedAt

**MaintenanceType**
- Id (Guid)
- Name
- Periodicity (Annual, Semestrial, etc.)
- DeviceId → Device

**MaintenanceInstance**
- Id (Guid)
- Date
- Cost
- Provider
- Notes
- Status
- MaintenanceTypeId → MaintenanceType

**HouseMember** (Phase 2)
- Id (Guid)
- Role (Owner, CollaboratorRW, CollaboratorRO, Tenant)
- CanLogMaintenance (bool, default true)
- UserId → User
- HouseId → House
- Unique index on (UserId, HouseId)

**Invitation** (Phase 2)
- Id (Guid)
- Token (unique UUID string)
- Role (HouseRole)
- Status (Pending, Accepted, Expired, Revoked)
- ExpiresAt (7 days from creation)
- HouseId → House
- CreatedByUserId → User
- AcceptedByUserId → User (nullable)

## Design System

### Color Palette (from wireframes)

**Light Mode**:
- Primary: `hsl(239, 84%, 67%)` - Indigo/blue
- Background: `hsl(0, 0%, 98%)` - Off-white
- Card: `hsl(0, 0%, 100%)` - White
- Text: `hsl(222.2, 84%, 4.9%)` - Dark navy
- Muted: `hsl(220, 9%, 46%)` - Gray

**Dark Mode**:
- Primary: `hsl(239, 84%, 67%)` - Same indigo
- Background: `hsl(224, 71%, 4%)` - Dark navy
- Card: `hsl(224, 71%, 4%)` - Dark navy
- Text: `hsl(213, 31%, 91%)` - Light gray

### Design Specs
- **Border Radius**: `0.75rem` (12px) for rounded corners
- **Typography**: Clean, modern sans-serif
- **Shadows**: Subtle drop shadows on cards
- **Spacing**: Generous whitespace
- **Icons**: Simple line icons with circular backgrounds

## Internationalization (i18n)

**Languages**: French (fr) and English (en)

**Translation Files**:
- `src/HouseFlow.Frontend/src/messages/fr.json`
- `src/HouseFlow.Frontend/src/messages/en.json`

**Usage**:
```tsx
import { useTranslations } from 'next-intl';

const t = useTranslations('namespace');
const tCommon = useTranslations('common');
```

**Namespaces**:
- `common`: loading, error, save, cancel, viewDetails, optional, etc.
- `auth`: login, register, email, password, etc.
- `dashboard`: welcome, myHouses, noHousesYet, etc.
- `houses`: title, addHouse, members, notFound, etc.
- `devices`: title, addDevice, noDevicesYet, createError, etc.
- `maintenance`: title, logMaintenance, history, etc.

**URL Locale Switching**:
```
/fr/dashboard → Français
/en/dashboard → English
```

## Dark Mode

Enabled via `next-themes`. Users can toggle between light, dark, and system themes.

**Usage**:
```tsx
import { useTheme } from 'next-themes';

const { theme, setTheme } = useTheme();
setTheme('dark'); // 'light' | 'dark' | 'system'
```

## Loading UX (Skeleton Loaders)

All pages use skeleton loaders instead of "Loading..." text for better perceived performance.

**Skeleton Components** (`src/HouseFlow.Frontend/src/components/ui/skeleton.tsx`):
- `Skeleton` — Base animated placeholder (Tailwind `animate-pulse`)
- `CardSkeleton` — House/device cards
- `HousesGridSkeleton` — 3-column grid for houses list
- `HouseDetailSkeleton` — House detail page (breadcrumb, header, device list)
- `DeviceDetailSkeleton` — Device detail page (header, maintenance types, history)
- `DashboardSkeleton` — Dashboard (hero, upcoming tasks, houses grid)
- `ListItemSkeleton` — Maintenance/device list items

**Loading Spinner** (`src/HouseFlow.Frontend/src/components/ui/loading-spinner.tsx`):
- `LoadingSpinner` — Animated spinner (sm/md/lg)
- `LoadingState` — Spinner with optional text label

**Usage pattern** (TanStack Query):
```tsx
const { data, isLoading } = useHouses();
if (isLoading) return <HousesGridSkeleton />;
```

**Button loading states** use `tCommon('loading')` text while `isPending` (forms, dialogs).

## Running the Application

### Prerequisites
- .NET 10 SDK
- Node.js 20+
- PostgreSQL 16 (optional, Aspire can start it)

### Development Mode

**Via Aspire (Recommended)**:
```bash
dotnet run --project src/HouseFlow.AppHost
```
This starts:
- PostgreSQL (port 5432)
- HouseFlow.API (port 5203)
- HouseFlow.Frontend (port 3000)
- Aspire Dashboard (port 15000)

**Default Admin User** (Development only):
- Email: `admin@admin.com`
- Password: `admin`
- Auto-created on first API startup in Development environment

**Manual Mode**:
```bash
# Terminal 1: Backend
cd src/HouseFlow.API
dotnet run

# Terminal 2: Frontend
cd src/HouseFlow.Frontend
npm run dev
```

### Testing

**Backend Tests**:
```bash
dotnet test
```

**Frontend E2E Tests**:
```bash
cd src/HouseFlow.Frontend
npm test              # All tests
npm run test:ui       # Interactive mode
npm run test:debug    # Debug mode
```

**Current Test Status**:
- Backend: 151 tests passing (7 unit + 144 integration)
- Frontend unit: 82 tests passing
- Frontend E2E: 37 tests passing

## Recent Changes (2026-03-31)

### Loading Skeletons (#40)
- Replaced last remaining "Loading..." text (houses list page) with `HousesGridSkeleton`
- All pages now use skeleton loaders: dashboard, house detail, device detail, houses list
- Documented skeleton component inventory and usage patterns in PROJECT_KNOWLEDGE.md

### API Retry Logic (#42)
1. **Axios interceptor** (`src/lib/api/client.ts`): Exponential backoff (100ms→200ms→400ms) with ±25% jitter, max 3 attempts
2. **Idempotent methods only**: GET, PUT, DELETE, HEAD, OPTIONS are retried; POST/PATCH are not (non-idempotent)
3. **Retryable errors**: 5xx, network errors, timeouts. 4xx errors are never retried
4. **UI indicator** (`components/ui/retry-indicator.tsx`): Amber banner with spinner shown during retries
5. **React Query**: Disabled built-in retry (handled at Axios level to avoid double-retrying)
6. **State tracking**: `onRetryStateChange` listener pattern + `useRetryState` hook for UI binding

## Recent Changes (2026-03-29)

### Security Hardening (#52, #49, #46)
1. **Docker image pinning** (#52): All Dockerfiles now use SHA256 digests (devcontainer, API, frontend)
2. **CORS restriction** (#49): Replaced `AllowAnyMethod`/`AllowAnyHeader` with explicit `WithMethods(GET, POST, PUT, DELETE)` and `WithHeaders(Authorization, Content-Type)`
3. **PII sanitization** (#46): New `scripts/sanitize-pii.sh` anonymises Users, RefreshTokens, AuditLogs, and Invitations for prod→preprod sync. Includes prod safety guard.

### Terraform State Split: Isolate Prod/Preprod from Shared Infra
1. **State separation** (`infrastructure/terraform/`):
   - `main/` → shared infra only (VNet, PostgreSQL, CAE, identity, bastion) — `main.tfstate`
   - `deploy-prod/` → prod Container Apps (ca-api-prod, ca-frontend-prod) — `deploy-prod.tfstate`
   - `deploy-preprod/` → preprod Container Apps (ca-api-preprod, ca-frontend-preprod) — `deploy-preprod.tfstate`
   - `ephemeral/` → PR preview environments — `ephemeral-pr-{N}.tfstate` (one state per PR)
   - Deploy directories read shared resources via `terraform_remote_state` from `main.tfstate`

2. **Workflow changes**:
   - `deploy.yml` → each job targets its own Terraform directory with simple `api_image_tag`/`frontend_image_tag` variables
   - `infra.yml` (new) → plan-only on push to `main`, apply via manual `workflow_dispatch` with production approval gate
   - `migrate-container-apps.yml` (one-time) → imports Container Apps into new states, removes from `main.tfstate`
   - Removed `migrate-state.yml` (obsolete one-time state split workflow)

3. **Benefits**:
   - Deploying preprod can no longer accidentally update prod Container Apps
   - Each environment has its own state lock — no concurrency conflicts
   - Infrastructure changes require manual approval, not auto-applied on every deploy

## Recent Changes (2026-03-27)

### VNet Integration & Entra ID Passwordless Auth
1. **Network** (`infrastructure/terraform/network.tf`):
   - VNet 10.0.0.0/16 with delegated subnets (Container Apps /23, PostgreSQL /28)
   - Private DNS Zone for PostgreSQL internal resolution
   - PostgreSQL no longer publicly accessible

2. **Entra ID Auth** (`infrastructure/terraform/identity.tf`, `src/HouseFlow.API/Program.cs`):
   - User-assigned managed identity for Container Apps → PostgreSQL
   - `DefaultAzureCredential` + `UsePeriodicPasswordProvider` for automatic token refresh
   - Password auth disabled on PostgreSQL — Entra ID only
   - Added `Azure.Identity` NuGet package

3. **Bastion** (`infrastructure/terraform/bastion.tf`):
   - SSH tunnel Container App (scale-to-zero) for DBeaver/psql access to private DB
   - Image pinned to `linuxserver/openssh-server:version-10.2_p1-r0`

4. **Security**:
   - Targeted CanNotDelete locks on prod apps + prod/preprod databases (not RG-level)
   - CORS fix: `SetIsOriginAllowed(_ => true)` when origin is wildcard (spec-compliant)

## Recent Changes (2026-03-26)

### US-062: Azure Container Apps Deployment with Terraform
1. **Terraform Infrastructure** (`infrastructure/terraform/`):
   - Provider azurerm ~4.0 with OIDC backend
   - Separate states: `main/` (shared infra), `deploy-prod/`, `deploy-preprod/`, `ephemeral/`
   - PostgreSQL Flexible Server (B1ms) with prod + preprod databases
   - Container Apps Environment shared across all environments
   - Management locks (CanNotDelete) on prod Container Apps and prod/preprod databases
   - `prevent_destroy` lifecycle on prod resources
   - GHCR registry credentials via PAT

2. **Workflows**:
   - `deploy.yml`: Build & push to GHCR → deploy preprod → manual approval → deploy prod
   - `infra.yml`: Plan on push, apply via manual dispatch with approval
   - Health checks via Container App URLs (`/alive` endpoint)

3. **Security**:
   - Custom RBAC role "HouseFlow Deployer" (not Contributor)
   - Azure Policies: resource type allowlist + PostgreSQL SKU restriction
   - Setup guide: `docs/azure-setup-guide.md`

### US-063: Ephemeral PR Preview Environments
1. **Terraform Module** (`infrastructure/terraform/modules/ephemeral-env/`):
   - Creates Container Apps + database per PR
   - Shared Container Apps Environment and PostgreSQL server

2. **PR Preview Workflow** (`.github/workflows/pr-preview.yml`):
   - Auto-deploy on PR open/sync, auto-destroy on PR close
   - Max 3 simultaneous preview environments
   - Posts preview URL as PR comment

3. **Local Test Environment** (`docker-compose.test.yml`):
   - Full-stack Docker Compose (API + Frontend + PostgreSQL)
   - `docker compose -f docker-compose.test.yml up --build`

## Recent Changes (2026-03-18)

### Phase 2: Security Hardening & Background Jobs
1. **Hangfire Background Jobs**:
   - Added Hangfire with PostgreSQL storage (separate `hangfire` schema)
   - `CleanupExpiredInvitationsJob`: daily job that marks expired invitations and deletes old ones (>30 days)
   - Dashboard available at `/hangfire` in Development only
   - Packages: `Hangfire.AspNetCore`, `Hangfire.PostgreSql`, `Hangfire.Core`

2. **Security Fixes**:
   - Cryptographic invitation tokens (32 bytes via `RandomNumberGenerator`)
   - Serializable transactions for invitation acceptance (race condition prevention)
   - Token redaction for non-owner users
   - Max 20 pending invitations per house
   - Self-accept prevention, inviter name masking
   - `CanViewCosts` permission for tenants (new DB column + migration)

3. **CI Improvements**:
   - Added `JunitXml.TestLogger` to test projects for CI test reports
   - `dorny/test-reporter` for backend, frontend unit, and E2E test results
   - Added `permissions: checks: write` to workflow

## Recent Changes (2026-03-17)

### Phase 2: Collaboration
1. **Backend - RBAC & Invitation System**:
   - New entities: `HouseMember` (join table with Role + CanLogMaintenance), `Invitation` (UUID token, 7-day expiry)
   - New enums: `HouseRole` (Owner, CollaboratorRW, CollaboratorRO, Tenant), `InvitationStatus`
   - `HouseMemberService`: full RBAC with `GetUserRoleAsync`, `EnsureAccessAsync`, invitation CRUD
   - Backward-compatible: `House.UserId` still indicates owner, `GetUserRoleAsync` checks it first
   - `CreateHouseAsync` and `RegisterAsync` now create both House AND HouseMember(Owner) records
   - Role-based access on all endpoints (devices, maintenance, houses)
   - Tenant cost/provider hiding in maintenance history
   - Configurable `canLogMaintenance` for tenants
   - New endpoints: Members CRUD, Invitations CRUD, `/api/v1/collaborators`
   - EF Core migration: `AddCollaborationTables`

2. **Frontend - Collaboration UI**:
   - New API hooks: `useHouseMembers`, `useAllCollaborators`, `useCreateInvitation`, `useAcceptInvitation`, etc.
   - `HouseSummaryDto` and `HouseDetailDto` now include `userRole`
   - Invitation acceptance page at `/{locale}/invitations/{token}`
   - Members management section on house detail page (create invitations, copy links, manage roles)
   - Shared house badges on dashboard cards
   - Role-based UI: hide edit/delete for non-owners, hide add device for read-only
   - Register form supports `?invitation=` query param
   - i18n keys for collaboration features (fr + en)

3. **Tests**: 21 new integration tests for collaboration (invitations, members, RBAC access control)

### Session Initialization (Claude Code Web)
- Added `scripts/init-session.sh` - auto-starts Docker, restores .NET deps, installs npm deps & Playwright
- Added `.claude/settings.json` with SessionStart hook (runs on each new web session)
- **Required network whitelist** for full test execution:
  - Docker Hub: `registry-1.docker.io`, `auth.docker.io`, `*.cloudflarestorage.com`
  - Playwright: `cdn.playwright.dev`, `playwright.download.prss.microsoft.com`

### US-045: Upcoming Tasks (Dashboard)
- Added `limit` query parameter to `GET /api/v1/upcoming-tasks`
- Fixed sorting: tasks never done (null NextDueDate) now appear first
- Frontend dashboard uses `limit=5`
- 3 new integration tests (sorted by date, respects limit, user isolation)

## Recent Changes (2026-03-11)

### Backend Stabilization
1. **Code Review & Fixes**:
   - Fixed AuthController to return 409 Conflict for duplicate email registration
   - Fixed DevicesController.GetDeviceMaintenanceTypes missing 403 handler
   - Added 6 new tests for revoke/logout endpoints
   - Added tests for Custom periodicity, DTO validation, authorization

2. **MaintenanceCalculatorService Extraction**:
   - Created `IMaintenanceCalculatorService` interface
   - Centralized score/status calculation logic from HouseService, DeviceService, MaintenanceService
   - Methods: CalculateNextDueDate, CalculateMaintenanceTypeStatus, CalculateDeviceScore, CalculateHouseScore

3. **Database Schema Simplification**:
   - Removed Organizations and HouseMembers tables
   - Direct User → House ownership (UserId on House)
   - User: Name split into FirstName + LastName
   - Device: Metadata replaced with Brand + Model fields

4. **Default Admin User**:
   - Auto-seeded in Development environment only
   - Credentials: admin@admin.com / admin

### Test Coverage
- 85 backend tests passing (7 unit + 78 integration)
- 70 E2E tests passing
- Tests use InMemory database (doesn't check migrations)

## Recent Changes (2026-03-31)

### Backend Code Generation from OpenAPI (#39)
- Added NSwag v14.6.3 as dotnet local tool for server-side code generation
- Two NSwag configs: `nswag-dtos.json` (DTOs) and `nswag-controllers.json` (controller bases)
- Generated DTOs in `Application/Generated/Contracts.g.cs` (namespace `HouseFlow.Contracts`)
- Generated controller base classes in `API/Generated/Controllers.g.cs`
- MSBuild targets auto-regenerate when `specs/openapi.yaml` changes
- Migrated 7 request DTOs to generated types via global using aliases in `ContractAliases.cs`
- Updated OpenAPI spec: added User theme/language, HouseSummary userRole, password pattern
- Helper script: `scripts/generate-api.sh`

## Previous Changes (2026-03-23)

### Separate DB Migrations from API Startup (#45)
- Removed auto-migration (`Database.Migrate()`) from API startup
- Added `--migrate` CLI mode: `dotnet HouseFlow.API.dll --migrate` runs migrations then exits
- Docker-compose (preprod/prod) now use a `migrate` init container that runs before the API starts
- API depends on `migrate` with `service_completed_successfully` condition
- Integration tests (Testing env) still auto-migrate via Program.cs
- CI E2E tests already used `dotnet ef database update` separately

## Previous Changes (2025-12-25)

### API-First Workflow Implementation
1. Updated `openapi.yaml`:
   - Made house address fields optional (address, zipCode, city)
   - Added `firstHouseId` to `AuthResponse`
2. Regenerated frontend TypeScript client from OpenAPI
3. Updated backend DTOs and entities to match spec
4. Documented API-First workflow in README.md

### UI/UX Improvements
1. Analyzed wireframes for design specifications
2. Updated color palette to indigo-based theme
3. Increased border radius from 0.5rem to 0.75rem
4. Updated both light and dark mode color schemes

### Internationalization Fixes
1. Added missing translation keys to en.json and fr.json
2. Updated components to use translation keys
3. Eliminated English/French mixing throughout the application

### Feature Implementations
1. **Auto-Create First House**: On registration, creates "Ma Maison"
2. **Single House Auto-Redirect**: Users with 1 house are redirected to house details
3. **Optional Address Fields**: Address, zipCode, city no longer required
4. **Device Creation Flow**: Redirects to device creation after registration

## Known Issues

None currently - all tests passing.

## File Locations

### Configuration
- OpenAPI Spec: `analyse_technique/openapi.yaml`
- Frontend Config: `src/HouseFlow.Frontend/openapi-ts.config.ts`
- Tailwind Config: `src/HouseFlow.Frontend/tailwind.config.ts`
- i18n Messages: `src/HouseFlow.Frontend/src/messages/{fr,en}.json`
- Rider Run Configs: `.idea/.idea.HouseFlow/.idea/runConfigurations/`

### Key Backend Files
- Auth Service: `src/HouseFlow.Infrastructure/Services/AuthService.cs`
- House Service: `src/HouseFlow.Infrastructure/Services/HouseService.cs`
- Device Service: `src/HouseFlow.Infrastructure/Services/DeviceService.cs`
- Maintenance Service: `src/HouseFlow.Infrastructure/Services/MaintenanceService.cs`
- Maintenance Calculator: `src/HouseFlow.Infrastructure/Services/MaintenanceCalculatorService.cs`
- DTOs: `src/HouseFlow.Application/DTOs/`
- Entities: `src/HouseFlow.Core/Entities/`
- Migrations: `src/HouseFlow.Infrastructure/Migrations/`

### Key Frontend Files
- API Client: `src/HouseFlow.Frontend/src/lib/api/generated/`
- Hooks: `src/HouseFlow.Frontend/src/lib/api/hooks/`
- Pages: `src/HouseFlow.Frontend/src/app/[locale]/(dashboard)/`
- Styles: `src/HouseFlow.Frontend/src/app/globals.css`
- Auth Context: `src/HouseFlow.Frontend/src/lib/auth/`
- Validations: `src/HouseFlow.Frontend/src/lib/validations/`

### Frontend Structure
```
src/HouseFlow.Frontend/src/
├── app/[locale]/          # Next.js App Router with i18n
│   ├── (auth)/           # Auth pages (login, register)
│   ├── (dashboard)/      # Protected dashboard pages
│   └── layout.tsx        # Root layout with providers
├── components/
│   ├── ui/               # Shadcn/ui components (incl. skeleton loaders)
│   ├── providers/        # React context providers
│   └── ...               # Feature components
├── lib/
│   ├── api/              # Generated OpenAPI client + hooks
│   ├── auth/             # Auth context
│   ├── i18n/             # Internationalization config
│   ├── utils/            # Utility functions
│   └── validations/      # Zod schemas
└── messages/             # i18n translations (en.json, fr.json)

e2e/
├── fixtures/             # Playwright fixtures (auth, db)
├── pages/                # Page Object Models
└── tests/                # E2E test suites
```

### Frontend Commands
```bash
npm run dev              # Dev server
npm run build            # Build production
npm run start            # Start production
npm run lint             # ESLint
npm test                 # Tests Playwright
npm run test:ui          # Tests mode interactif
npm run generate-client  # Générer client API
```

## Environment Variables

```env
# Backend (appsettings.Development.json)
ConnectionStrings__DefaultConnection=Host=localhost;Port=5432;Database=houseflow;Username=postgres;Password=yourpassword
Jwt__Key=YourSuperSecretKeyForJWTTokenGeneration123456
Jwt__Issuer=HouseFlowAPI
Jwt__Audience=HouseFlowClient

# Frontend (.env.local)
NEXT_PUBLIC_API_URL=http://localhost:5203
```

## Development Guidelines

### Code Style
- Use C# 13 features (required, init, records)
- Follow Onion Architecture principles
- Keep DTOs in Application layer
- Keep entities in Core layer
- Use async/await for all I/O operations

### Testing
- Write E2E tests for all user flows
- Test both French and English locales
- Test all device types
- Test permission matrix

### Git Workflow
- Main branch: `main`
- Feature branches: `feature/description`
- Always run tests before committing
- Use conventional commits

### Security
- Never commit secrets
- Use Azure Key Vault for production
- Validate all user inputs
- Use parameterized queries (EF Core handles this)
- Hash passwords with BCrypt

## Future Improvements

### Feature Backlog
1. Email notifications for upcoming maintenance
2. File attachments for maintenance logs
3. Stripe integration for premium subscriptions
4. Multi-house selection (if more than 1)
5. Export maintenance history to PDF/CSV

### Technical Debt
1. Set up backend code generation from OpenAPI (currently manual)
2. Add more comprehensive error handling
3. Implement retry logic for API calls
4. ~~Add loading skeletons instead of "Loading..." text~~ ✅ Done (issue #40)
5. Implement optimistic UI updates

## Contact & Resources

- **Quick Start**: `README.md`
- **Specifications**: `specs/` (requirements, user-stories, architecture, openapi)
- **Wireframes**: `specs/wireframes/`
- **Task Management**: [GitHub Issues](https://github.com/BarbeRouss/HouseFlow/issues) + [Milestones](https://github.com/BarbeRouss/HouseFlow/milestones)
- **Lessons Learned**: `tasks/lessons.md`

---

**Note**: This is a living document. Update it whenever significant changes are made to the project.
