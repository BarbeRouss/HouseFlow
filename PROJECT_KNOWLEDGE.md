# HouseFlow - Project Knowledge Base

**Last Updated**: 2026-03-17

## Project Overview

**HouseFlow** is a home maintenance tracking application that helps users manage multiple properties, devices, and maintenance schedules. Built with .NET 10 backend and Next.js 15 frontend.

## Technology Stack

### Backend
- **.NET 10** with C# 13
- **ASP.NET Core Web API**
- **Entity Framework Core 10** with PostgreSQL
- **Aspire 13.1.0** for orchestration and observability
- **NSwag** for OpenAPI/Swagger documentation
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
- **Azure Container Apps** (deployment target)
- **Azure Database for PostgreSQL**

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

1. **Update OpenAPI Spec** (`analyse_technique/openapi.yaml`)
2. **Regenerate Frontend Client**:
   ```bash
   cd src/HouseFlow.Frontend
   npm run generate-client
   ```
3. **Update Backend Code** manually to match spec:
   - DTOs in `Application/DTOs/`
   - Entities in `Core/Entities/`
   - Services in `Infrastructure/Services/`
4. **Run Tests** to verify everything works

**Source of Truth**: `analyse_technique/openapi.yaml`

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
- Backend: 115 tests passing (7 unit + 108 integration)
- Frontend E2E: 70 tests passing

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

## Recent Changes (2025-12-25)

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
│   ├── ui/               # Shadcn/ui components
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
4. Add loading skeletons instead of "Loading..." text
5. Implement optimistic UI updates

## Contact & Resources

- **Quick Start**: `README.md`
- **Specifications**: `specs/` (requirements, user-stories, architecture, openapi)
- **Wireframes**: `specs/wireframes/`
- **Task Management**: [GitHub Issues](https://github.com/BarbeRouss/HouseFlow/issues) + [Milestones](https://github.com/BarbeRouss/HouseFlow/milestones)
- **Lessons Learned**: `tasks/lessons.md`

---

**Note**: This is a living document. Update it whenever significant changes are made to the project.
