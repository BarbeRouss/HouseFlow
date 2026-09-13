# HouseFlow - Project Knowledge Base

**Last Updated**: 2026-09-12

## Project Overview

**HouseFlow** is a home maintenance tracking application that helps users manage multiple properties, devices, and maintenance schedules. Built with a .NET 10 backend and a Blazor WebAssembly frontend (`src/HouseFlow.Web`).

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

### Frontend (`src/HouseFlow.Web`)
- **Blazor WebAssembly** (standalone, .NET 10, client-side rendering)
- **Blazor Blueprint** component library (`BlazorBlueprint.Components` / `.Icons.Lucide`) referenced + `AddBlazorBlueprintComponents()`; the app's own UI is built with custom Razor components on the **same Tailwind CSS v3 design system** (copied `globals.css`/tailwind config — indigo primary `239 84% 67%`, radius 0.75rem) to preserve the exact charte graphique and satisfy the DOM/class-based E2E selectors. Tailwind is compiled via `src/HouseFlow.Web/package.json` (`npm run build:css` → `wwwroot/css/app.css`).
- **Auth**: in-memory + localStorage/sessionStorage token store (`Auth/TokenStore`, registered **singleton** — a scoped store would give `IHttpClientFactory`'s handler a different instance), custom `AuthenticationStateProvider`, `AuthMessageHandler` (bearer + credentials-include + refresh-on-401).
- **i18n**: JSON message catalogs embedded from `Localization/Resources/{fr,en}.json` (copied from the old `src/messages`), resolved by `Localizer` (`{var}` + simple ICU plural); locale = first URL segment.
- **Served in dev/E2E** via the WASM dev server on :3000 (`scripts/dev-web.sh`); via `HouseFlow.WebHost` under Aspire.
- **Deployed (preprod/prod)** as the `houseflow-frontend` Docker image built from `src/HouseFlow.WebHost/Dockerfile` (repo-root context): the WASM app is published *standalone* (only that publish resolves the `index.html` fingerprint placeholders), then its `wwwroot` is overlaid on the published `HouseFlow.WebHost`, which serves it on :3000. The host exposes `/appsettings.json` from the `API_BASE_URL` / `DEMO_MODE` environment variables (`WebHost/Program.cs`), so the same image serves preprod and prod — Terraform sets `API_BASE_URL` on each frontend Container App. PR previews use Azure Static Web Apps instead (no image, see `pr-preview.yml`).
- **Playwright** E2E at repo-root `e2e/` (49 scenarios); run with `bash scripts/verify-e2e.sh`.

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
- **Claude Code routine** fired by `claude-issue.yml` — a cloud session starts on an issue when the `claude` label is added
- **GHCR** for container images (PAT `read:packages` for Azure pull)
- **Bastion Container App** (SSH tunnel, scale-to-zero) for private DB access via DBeaver

## Architecture

### Onion Architecture Layers

```
src/
├── HouseFlow.Core/              # Domain entities and interfaces
│   ├── Entities/                # House, Device, User, etc.
│   └── Enums/                   # HouseRole, InvitationStatus
├── HouseFlow.Application/       # Business logic, DTOs, and persistence port
│   ├── DTOs/                    # Data Transfer Objects
│   ├── Interfaces/              # Service interfaces + IApplicationDbContext (persistence port)
│   └── Services/                # AuthService, HouseService, DeviceService, etc. (real application services)
├── HouseFlow.Infrastructure/    # Technical adapters only — no business logic
│   ├── Data/                    # EF Core DbContext (implements IApplicationDbContext)
│   ├── Migrations/              # EF Core migrations
│   └── Jobs/                    # Hangfire recurring jobs
├── HouseFlow.API/              # REST API controllers (composition root)
│   └── Controllers/             # API endpoints
├── HouseFlow.AppHost/          # Aspire orchestration
├── HouseFlow.Web/              # Blazor WebAssembly frontend (.razor components)
└── HouseFlow.WebHost/          # ASP.NET Core host serving HouseFlow.Web static web assets
```

**Note (2026-08-19)**: Business logic previously lived in `Infrastructure/Services/` (an onion
violation — Infrastructure implementing Application's interfaces). It has been moved to
`Application/Services/`. Services now depend on `IApplicationDbContext`
(`Application/Interfaces/IApplicationDbContext.cs`) instead of the concrete EF Core
`HouseFlowDbContext`, so Application never references Npgsql or the Infrastructure project —
only the EF Core abstractions (`Microsoft.EntityFrameworkCore` + `.Relational`, needed for
`DbSet<T>` and transaction isolation levels). `HouseFlowDbContext` implements the interface.
`ApiKeyAuthenticationHandler` and `AuditContextMiddleware` in the API layer still use the
concrete `HouseFlowDbContext` directly — that's fine since API is the composition root.

### API-First Development Workflow

**CRITICAL**: This project follows an **API-First (Contract-First)** approach:

1. **Update OpenAPI Spec** (`specs/openapi.yaml`)
2. **Frontend consumes generated contracts**: the Blazor WebAssembly app (`src/HouseFlow.Web`) is C# and references the DTOs generated in step 3 below (`HouseFlow.Contracts`) — there is no separate TypeScript client to regenerate.
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
   - Services in `Application/Services/`
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
- Theme / Language (preferences)
- IsAdmin (bool, default false — platform administrator, see "Administration")
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

**Translation Files** (JSON catalogs embedded into the Blazor app):
- `src/HouseFlow.Web/Localization/Resources/fr.json`
- `src/HouseFlow.Web/Localization/Resources/en.json`

**Usage**: components inherit `Components/AppComponentBase` and call its `T(...)` helper, which resolves keys through `Localization/LocalizationState` + `Localization/Localizer.cs` (`{var}` substitution and simple ICU plurals). The base component also re-renders on locale change.
```razor
@inherits AppComponentBase

<h1>@T("dashboard.welcome")</h1>
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

Managed by `ThemeService` (`src/HouseFlow.Web/ThemeService.cs`), which applies the `dark`/`light` class on `<html>` (via `hf.applyTheme` in `wwwroot/js/app.js`) and persists the choice in `localStorage`. Users toggle between light, dark, and system themes through the `Components/ThemeToggle.razor` component. Same indigo palette in both modes.

**Usage**:
```razor
@inject ThemeService Theme

<button @onclick='() => Theme.SetThemeAsync("dark")'>Dark</button>
@* Theme.Current is one of "light" | "dark" | "system" *@
```

## Loading UX

All pages show animated skeleton placeholders (Tailwind `animate-pulse`) instead of "Loading..." text for better perceived performance.

**Pattern**: each feature page (`src/HouseFlow.Web/Features/**/*.razor`) tracks a `_loading` flag, sets it `false` once the API call returns, and renders skeleton markup inside an `@if (_loading)` block while data loads:
```razor
@if (_loading)
{
    <div class="animate-pulse ...">...</div>
}
else
{
    @* real content *@
}
```

**Retry indicator**: `Components/RetryIndicator.razor` (backed by `Api/RetryState.cs`) shows a "reconnecting" banner while a transient request is being retried.

**Button loading states** use the localized `common.loading` text while a form/dialog submit is in flight.

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
- HouseFlow.WebHost (Blazor WebAssembly frontend, port 3000)
- Aspire Dashboard (port 15000)

**Default Admin User** (Development only):
- Email: `admin@admin.com`
- Password: `admin`
- Auto-created on first API startup in Development environment
- Note: this seeded account is *not* a platform administrator (`IsAdmin`); see "Administration" below.

**Administration (platform admins)**:
- Users flagged `IsAdmin` get the `Admin` role claim in their JWT and can open `/{locale}/admin`
  (`Features/Admin/AdminPage.razor`) — global stats + user list with search/pagination + grant/revoke admin.
- Backend: `AdminController` (`/api/v1/admin/*`, `[Authorize(Roles = "Admin")]`) → `IAdminService`/`AdminService`.
  API keys never carry the role, so they can't reach admin endpoints. Role changes take effect at the target
  user's next login / JWT refresh (15 min max).
- First admin(s) come from configuration `Admin:BootstrapEmails` (`src/HouseFlow.API/appsettings.json`, currently
  `julienrousselle@outlook.be`; overridable with `Admin__BootstrapEmails__N` env vars). When `DEMO_MODE=true`
  (PR previews, local dev — never production) the seeded demo account `demo@demo.com` is an admin too. `AdminBootstrap`
  (`Application/Common`) reads it: existing accounts are promoted at API startup (`PromoteBootstrapAdminsAsync`),
  new ones at registration. `scripts/dev-api.sh` and the CI E2E job add `e2e-admin@houseflow.test` (index 1) for
  the Playwright admin suite.

**Manual Mode**:
```bash
# Terminal 1: Backend
cd src/HouseFlow.API
dotnet run

# Terminal 2: Frontend (Blazor WebAssembly dev server on :3000)
bash scripts/dev-web.sh
# (compile Tailwind CSS when styles change: cd src/HouseFlow.Web && npm run build:css)
```

### Testing

**Backend Tests**:
```bash
dotnet test
```

**Frontend E2E Tests** (Playwright, suites at repo-root `e2e/`):
```bash
bash scripts/verify-e2e.sh   # starts the API + Blazor frontend if needed, then runs all scenarios
```

**Current Test Status** (backend, verified 2026-09-11):
- Backend: 203 tests passing (45 unit + 158 integration)

## Recent Changes (2026-09-11)

### Admin interface (US-400 (#191))
- `User.IsAdmin` (migration `20260911193042_AddIsAdminToUser`, default false).
- `AuthService` issues a `role: Admin` claim in JWTs of admins; `UserDto`/`AuthResponse` expose `isAdmin`
  (the Blazor `AuthUser` stores it; the header shows an "Administration" link for admins).
- New `AdminService` + `AdminController` (`GET /api/v1/admin/stats`, `GET /api/v1/admin/users?search&page&pageSize`,
  `PUT /api/v1/admin/users/{id}/admin`), documented in `specs/openapi.yaml` (tag Admin; `SetUserAdminRequest`
  generated + aliased). Self-demotion is refused (400); unknown user → 404.
- Bootstrap admins via `Admin:BootstrapEmails` (appsettings.json → `julienrousselle@outlook.be`): promoted at API
  startup if the account exists, at registration otherwise.
- New page `Features/Admin/AdminPage.razor` (stats tiles via `Components/StatCard.razor`, user list with search,
  pagination and confirm modal for grant/revoke) + `admin.*` i18n keys.
- Tests: `tests/HouseFlow.IntegrationTests/Admin/AdminTests.cs` (9 tests: 401/403 incl. API key, bootstrap flag,
  stats, search/pagination, grant/revoke, self-demotion, 404) and `e2e/tests/admin.spec.ts` (4 scenarios, using the
  `e2e-admin@houseflow.test` bootstrap admin injected by `scripts/dev-api.sh` / CI).

## Recent Changes (2026-09-12)

### 2026-09-12 — Claude Code session on labeled issues (`claude-issue.yml` + routine)

Adding the `claude` label to an issue (label created on the repo) starts a **Claude Code cloud
session** whose only instruction is "handle issue #n by following CLAUDE.md". The mechanism:

- A Claude Code **routine** ("Correction issue HouseFlow", id `trig_017zpGmaCX8P9nKNdqqkni8h`,
  created from the routines web UI with `BarbeRouss/HouseFlow` attached, fresh session per fire,
  same cloud environment as the web sessions) holds the prompt. Its saved prompt reads the issue number from the fire payload
  (`issue=<n>`), reads the issue with `gh`, then follows CLAUDE.md end to end (complete the issue
  if needed, implement with tests, 3-step checklist, PR with `Closes #n`, CI watch via the steward
  skill). Ambiguous or oversized issues get a comment and no push.
- `.github/workflows/claude-issue.yml` is a ~20-line trigger: on `issues: labeled` with label
  `claude`, it POSTs `issue=<n>` to the routine's `/fire` endpoint (bearer token in the
  `CLAUDE_ROUTINE_TOKEN` secret) and comments the session URL on the issue. No toolchain on the
  runner — the session runs in the cloud environment with `init-session.sh`, hooks and skills.
- The label is applied by hand, after reading the issue — the human gate against prompt injection
  from untrusted issue bodies. Only the issue number crosses the API; the session reads the issue
  itself.
- Routines' native GitHub triggers only cover pull requests and releases (not issues), hence the
  API trigger. The `/fire` endpoint is in research preview (`anthropic-beta:
  experimental-cc-routine-2026-04-01`), and routine runs count against the account's daily cap.
  Commits and PRs from these sessions carry the routine owner's GitHub identity.
- One-time setup (done from the routines web UI, the in-session API cannot attach a repository):
  routine with `BarbeRouss/HouseFlow` as repository + an **API** trigger; its token lives in the
  `CLAUDE_ROUTINE_TOKEN` repository secret.

### Devcontainer constructible derrière un proxy TLS intercepteur (Claude Code web)

Le build de l'image devcontainer échouait en session Claude Code web (proxy d'egress
qui re-termine le TLS + hôte tournant en root). Corrigé : la CA du proxy est installée
tôt dans le build et le cas hôte root est géré — **no-op en build local**. Limite
connue : au runtime, le conteneur ne peut pas joindre le proxy explicite, donc
`dotnet restore` (et par conséquent `dotnet test`/`build` et `verify-e2e.sh`) ne tourne
pas dans le devcontainer d'une session web — validation via CI ou en local. La règle
« tout passe par le devcontainer » reste en vigueur.

Détails techniques et limite : `.devcontainer/README.md` (section « Derrière un proxy
TLS intercepteur »). Leçon associée : `tasks/lessons.md` (2026-09-12).

## Recent Changes (2026-08-19)

### 2026-09-12 — Deploy pipeline repaired for the Blazor frontend (issue #155)

`deploy.yml` still built the frontend image from the deleted Next.js directory
(`src/HouseFlow.Frontend`), so every Deploy run on `main` failed since the Blazor migration.

- New `src/HouseFlow.WebHost/Dockerfile` (standalone WASM publish + WebHost overlay, Tailwind
  built in a `node:22-alpine` stage, images pinned by digest) → `houseflow-frontend` image, port 3000.
- `HouseFlow.WebHost` serves `/appsettings.json` from `API_BASE_URL` / `DEMO_MODE` so one image
  works for every environment; Terraform env var `NEXT_PUBLIC_API_URL` → `API_BASE_URL`.
- Deploy health check now probes the frontend too; `pr.yml` gained a `docker-images` job that
  builds both images and smoke-tests the frontend one, so this class of breakage fails the PR.
- Root `.dockerignore` (node_modules, bin/obj, .git, e2e…) keeps both build contexts small.
- The "CI" and "Deploy Blazor POC" entries in the Actions tab are orphaned workflows (files
  deleted, old runs remain); they disappear once their runs are deleted.

### Onion architecture fix: business services moved from Infrastructure to Application

The 8 business-logic services (`AuthService`, `HouseService`, `DeviceService`,
`HouseMemberService`, `MaintenanceService`, `MaintenanceCalculatorService`,
`UserSettingsService`, `ApiKeyService`) previously lived in `Infrastructure/Services/` and
used the concrete EF Core `HouseFlowDbContext` directly — Infrastructure implementing
Application's interfaces, the reverse of what onion architecture requires.

They now live in `Application/Services/` and depend on a new persistence port,
`IApplicationDbContext` (`Application/Interfaces/IApplicationDbContext.cs`), instead of the
concrete DbContext. `HouseFlowDbContext` (`Infrastructure/Data/`) implements that interface.
Infrastructure is now limited to the EF Core/Postgres adapter, migrations, and the Hangfire
cleanup job — no business rules remain there.

`Application` gained package references to `Microsoft.EntityFrameworkCore` +
`.Relational` (EF Core abstractions only — `DbSet<T>`, transaction isolation levels — never
Npgsql), plus `BCrypt.Net-Next`, `System.IdentityModel.Tokens.Jwt`, and the
`Microsoft.Extensions.Configuration/Logging.Abstractions` packages the moved services already
depended on.

Frontend untouched. Full backend test suite (190 tests) verified green after the move, run via
`scripts/feature-env.sh` in the project devcontainer.

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

### Backend Code Generation from OpenAPI (#39)
- Added NSwag v14.6.3 as dotnet local tool for server-side code generation
- Two NSwag configs: `nswag-dtos.json` (DTOs) and `nswag-controllers.json` (controller bases)
- Generated DTOs in `Application/Generated/Contracts.g.cs` (namespace `HouseFlow.Contracts`)
- Generated controller base classes in `API/Generated/Controllers.g.cs`
- MSBuild targets auto-regenerate when `specs/openapi.yaml` changes
- Migrated 7 request DTOs to generated types via global using aliases in `ContractAliases.cs`
- Updated OpenAPI spec: added User theme/language, HouseSummary userRole, password pattern
- Helper script: `scripts/generate-api.sh`

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

### US-062 (#80): Azure Container Apps Deployment with Terraform
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

### US-063 (#88): Ephemeral PR Preview Environments
1. **Terraform Module** (`infrastructure/terraform/modules/ephemeral-env/`):
   - Creates Container Apps + database per PR
   - Shared Container Apps Environment and PostgreSQL server

2. **PR Preview Workflow** (`.github/workflows/pr-preview.yml`):
   - Auto-deploy on PR open/sync, auto-destroy on PR close
   - Max 3 simultaneous preview environments
   - Posts preview URL as PR comment

3. **Local Full-Stack Environment** (.NET Aspire):
   - `dotnet run --project src/HouseFlow.AppHost` orchestre API + Blazor WASM + PostgreSQL
   - En worktree/devcontainer : `scripts/feature-env.sh up <nom>` (conteneur dédié, ports assignés automatiquement)

### API Key Generation for External Integration (Phase 3)
- New `ApiKey` entity with SHA-256 hashed key storage, prefix-based lookup
- `ApiKeyScope` enum: `ReadOnly` / `ReadWrite`
- Dual authentication scheme: `PolicyScheme` forwards to JWT or API key handler based on request headers
- API key auth via `X-API-Key` header or `Authorization: Bearer hf_...`
- `ApiKeysController` with CRUD endpoints at `api/v1/users/api-keys`
- Global `ApiKeyScopeEnforcementFilter` blocks write operations for ReadOnly keys
- Max 5 active keys per user
- New `/settings` page with API key management UI
- i18n support (FR/EN) for all API key strings
- Settings link added to header dropdown menu
- EF migration: `AddApiKeys`

## Recent Changes (2026-03-23)

### Separate DB Migrations from API Startup (#45)
- Removed auto-migration (`Database.Migrate()`) from API startup
- Added `--migrate` CLI mode: `dotnet HouseFlow.API.dll --migrate` runs migrations then exits
- Docker-compose (preprod/prod) now use a `migrate` init container that runs before the API starts
- API depends on `migrate` with `service_completed_successfully` condition
- Integration tests (Testing env) still auto-migrate via Program.cs
- CI E2E tests already used `dotnet ef database update` separately

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

### US-045 (#64): Upcoming Tasks (Dashboard)
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
- Tailwind Config: `src/HouseFlow.Web/tailwind.config.js` (input `src/HouseFlow.Web/Styles/app.input.css` → output `src/HouseFlow.Web/wwwroot/css/app.css`)
- i18n Messages: `src/HouseFlow.Web/Localization/Resources/{fr,en}.json`
- Rider Run Configs: `.idea/.idea.HouseFlow/.idea/runConfigurations/`

### Key Backend Files
- Auth Service: `src/HouseFlow.Application/Services/AuthService.cs`
- Admin Service: `src/HouseFlow.Application/Services/AdminService.cs` (+ `Common/AdminBootstrap.cs`, `API/Controllers/AdminController.cs`)
- House Service: `src/HouseFlow.Application/Services/HouseService.cs`
- Device Service: `src/HouseFlow.Application/Services/DeviceService.cs`
- Maintenance Service: `src/HouseFlow.Application/Services/MaintenanceService.cs`
- Maintenance Calculator: `src/HouseFlow.Application/Services/MaintenanceCalculatorService.cs`
- Persistence port: `src/HouseFlow.Application/Interfaces/IApplicationDbContext.cs`
- DTOs: `src/HouseFlow.Application/DTOs/`
- Entities: `src/HouseFlow.Core/Entities/`
- DbContext (implements the persistence port): `src/HouseFlow.Infrastructure/Data/HouseFlowDbContext.cs`
- Migrations: `src/HouseFlow.Infrastructure/Migrations/`

### Key Frontend Files
- API Client: `src/HouseFlow.Web/Api/` (`ApiService.cs`, `Dtos.cs`, `RetryState.cs`)
- Pages (by feature): `src/HouseFlow.Web/Features/` (Admin, Auth, Dashboard, Devices, Houses, Invitations, Settings, Shared)
- Layouts: `src/HouseFlow.Web/Layout/` (`MainLayout`, `DashboardLayout`, `AuthLayout`)
- Shared Components: `src/HouseFlow.Web/Components/`
- Styles (Tailwind source): `src/HouseFlow.Web/Styles/app.input.css`
- Auth: `src/HouseFlow.Web/Auth/` (`TokenStore`, `AppAuthStateProvider`, `AuthMessageHandler`, `RedirectGuard`)
- Localization: `src/HouseFlow.Web/Localization/` (`Localizer`, `LocalizationState`, `Resources/{fr,en}.json`)
- Runtime config: `src/HouseFlow.Web/wwwroot/appsettings.json` → `AppConfig.cs`

### Frontend Structure
```
src/HouseFlow.Web/
├── Api/                   # ApiService (HttpClient wrapper), DTOs, RetryState
├── Auth/                  # TokenStore, AppAuthStateProvider, AuthMessageHandler, RedirectGuard
├── Components/            # Shared Razor components (Header, Modal, HfSelect, ThemeToggle, RetryIndicator, ...)
├── Features/              # Routable page components, grouped by area
│   ├── Auth/             # Login, Register
│   ├── Dashboard/        # Dashboard
│   ├── Devices/          # DeviceDetailPage, NewDevice
│   ├── Houses/           # HousesList, HouseDetailPage, NewHouse
│   ├── Invitations/      # AcceptInvitation
│   ├── Settings/         # Settings (API keys, preferences)
│   └── Shared/           # Landing, NotFoundPage
├── Layout/               # MainLayout, DashboardLayout, AuthLayout
├── Localization/         # Localizer, LocalizationState, Resources/{fr,en}.json
├── Styles/               # app.input.css (Tailwind source)
├── wwwroot/              # Static assets, compiled css/app.css, js/app.js, appsettings.json
└── App.razor, Program.cs, _Imports.razor, ThemeService.cs, AppConfig.cs

e2e/                       # (repo root) Playwright E2E
├── fixtures/             # Playwright fixtures (auth, db)
├── pages/                # Page Object Models
└── tests/                # E2E test suites
```

### Frontend Commands
```bash
# Tailwind CSS — the only npm scripts in src/HouseFlow.Web
# `dotnet build` compiles the CSS automatically (BuildTailwindCss MSBuild target),
# so these are only needed for a standalone compile or a live watch during dev.
npm run build:css        # Compile Tailwind (Styles/app.input.css → wwwroot/css/app.css)
npm run watch:css        # Recompile CSS on change

# Blazor build / run / test (dotnet + repo scripts, from repo root)
dotnet build src/HouseFlow.Web    # Build the WASM frontend
bash scripts/dev-web.sh           # Blazor WASM dev server on :3000
bash scripts/verify-e2e.sh        # Playwright E2E (starts API + frontend)
```

## Environment Variables

```env
# Backend (appsettings.Development.json)
ConnectionStrings__DefaultConnection=Host=localhost;Port=5432;Database=houseflow;Username=postgres;Password=yourpassword
Jwt__Key=YourSuperSecretKeyForJWTTokenGeneration123456
Jwt__Issuer=HouseFlowAPI
Jwt__Audience=HouseFlowClient
Admin__BootstrapEmails__0=julienrousselle@outlook.be   # accounts auto-promoted to platform admin (see "Administration")

# Frontend (src/HouseFlow.Web/wwwroot/appsettings.json — resolved into AppConfig at startup;
# written at build time from API_BASE_URL / DEMO_MODE by the WriteRuntimeConfig MSBuild target,
# and served from the same env vars at runtime by HouseFlow.WebHost — Aspire, Docker image)
{ "ApiBaseUrl": "http://localhost:5203", "DemoMode": "false" }
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
- **Specifications**: `specs/` (requirements, architecture, openapi) — le QUOI durable, sans statut d'avancement
- **Maquettes UX**: `specs/ux/`
- **Task Management**: [GitHub Issues](https://github.com/BarbeRouss/HouseFlow/issues) + [Milestones](https://github.com/BarbeRouss/HouseFlow/milestones)
- **Lessons Learned**: `tasks/lessons.md`

---

**Note**: This is a living document. Update it whenever significant changes are made to the project.
