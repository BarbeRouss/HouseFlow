# HouseFlow - Project Knowledge Base

**Last Updated**: 2026-09-29 (final visual design `specs/ux` applied — single UX source of truth, `docs/design/spec-refonte-v2.html` removed; new light/dark tokens, self-hosted fonts, « Système » theme, app icon + favicon by global status, PWA manifest, `HouseCard` / `DeviceTile` / `Breadcrumb` / `AppIconService`, `HouseRow` / `HouseSelector` / `StatusDot` removed, P06 name + frequency per device, P11 single column, M1 colour picker; house colour `House.colorKey` — 6-key palette, rotation per owner, `nextColorKey`, migration `AddHouseColorKey`; banner data `deviceTypes`, `membersCount`, `InvitationInfo.houseDeviceTypes`) — previously 2026-09-28 (RW collaborator can invite a tenant again — `capabilities.canInviteTenants`, M5 restricted mode; legal texts FR/EN rewritten for the invitee email and the tenant's edit right, policy version 2026-09-28; register points 11-12 closed) — previously 2026-09-27 (refonte UX complète, frontend + API — P01–P15, M1–M7, C1–C8, onboarding `/setup/house` → `/setup/devices`, nouvelles layouts / composants partagés / `Rules/`, R1 Europe/Paris, R2 échéance jamais nulle, `GET /dashboard`, R5 + `capabilities`, ProblemDetails `code`, invitations avec e-mail / refus / renvoi, inscription sans maison automatique — voir *Frontend Architecture* et Recent Changes ; migration Tailwind CSS v3 → v4 : configuration CSS-first dans `Styles/app.input.css`, CLI `@tailwindcss/cli` ; `LastLoginAt` écrit aussi au rafraîchissement de session — un utilisateur en « Se souvenir de moi » n'est plus qualifié inactif ; relecture juridique des pages légales : identification BCE/TVA, base légale de la preuve d'acceptation, destinataires — politique en version 2026-09-26 ; modes opératoires RGPD : test de restauration de sauvegarde et exercice de simulation de violation ; méthode d'établissement de la date du DPA Microsoft — RGPD #132–#139 : droits des personnes, rétention, consentement, registre des traitements ; #253 : spike `claude --cloud` depuis GitHub Actions — pas faisable, le dialogue d'une session automatisée passera par la PR ; #252 : `queue: max` sur le groupe de concurrence `ovh-dns-zone` — file d'attente réelle au lieu d'annulation ; #238 : DNS d'un environnement en racines à part, `dns` et `custom-domains`, pour que le verrou `ovh-dns-zone` ne couvre que les écritures OVH ; #198 : stratégie de retry EF Core alignée entre production et local ; #199 : dump nocturne pseudonymisé de la prod, restauré à la création de chaque environnement de PR)

## Project Overview

**HouseFlow** is a home maintenance tracking application that helps users manage multiple properties, devices, and maintenance schedules. Built with a .NET 10 backend and a Blazor WebAssembly frontend (`src/HouseFlow.Web`).

## Technology Stack

### Backend
- **.NET 10** with C# 13
- **ASP.NET Core Web API**
- **Entity Framework Core 10** with PostgreSQL
- **Aspire 13.5.4** for orchestration and observability
- **NSwag** for OpenAPI/Swagger documentation and backend code generation from spec
- **JWT** for authentication
- **BCrypt.Net** for password hashing
- **Onion Architecture** (Clean Architecture)

### Frontend (`src/HouseFlow.Web`)
- **Blazor WebAssembly** (standalone, .NET 10, client-side rendering)
- **Blazor Blueprint** component library (`BlazorBlueprint.Components` / `.Icons.Lucide`) referenced + `AddBlazorBlueprintComponents()` — only the Lucide icons are used; the app's own UI is built with custom Razor components (`Components/`, see *Frontend Architecture*) on a **Tailwind CSS v4** design system whose tokens live in the `@theme inline` block of `Styles/app.input.css` (hex CSS variables light/dark from `specs/ux/README.md` — primary #6366f1, background #f7f5f1 — plus radii, shadows, content widths; self-hosted Bricolage Grotesque + Instrument Sans; see *Design System*). Tailwind is compiled by `dotnet build` (MSBuild target `BuildTailwindCss`) or `npm run build:css` → `wwwroot/css/app.css`.
- **API client**: hand-written — `Api/Dtos.cs` + `Api/ApiService.cs` (no project reference to the backend, no generated client). `ApiException` exposes `StatusCode` + ProblemDetails `Code` (`ApiErrorCodes`).
- **Auth**: **in-memory only** token store (`Auth/TokenStore`, registered **singleton** — a scoped store would give `IHttpClientFactory`'s handler a different instance; nothing is written to `localStorage`/`sessionStorage`), custom `AuthenticationStateProvider`, `AuthMessageHandler` (bearer + credentials-include + refresh-on-401). `App.razor` calls `POST /auth/refresh` at every boot (reload, new tab, browser restart) to turn the HttpOnly refresh cookie into an access token — see "Sessions" below.
- **i18n**: JSON message catalogs embedded from `Localization/Resources/{fr,en}.json` (copied from the old `src/messages`), resolved by `Localizer` (`{var}` + simple ICU plural); locale = first URL segment.
- **Served in dev/E2E** by `HouseFlow.WebHost` on :3000 (`scripts/dev-web.sh`, devcontainer), as under Aspire; CI E2E (`pr.yml`) still uses the WASM dev server.
- **Deployed (preprod/prod)** as the `houseflow-frontend` Docker image built from `src/HouseFlow.WebHost/Dockerfile` (repo-root context): the WASM app is published *standalone* (only that publish resolves the `index.html` fingerprint placeholders), then its `wwwroot` is overlaid on the published `HouseFlow.WebHost`, which serves it on :3000. The host exposes `/appsettings.json` from the `API_BASE_URL` / `DEMO_MODE` environment variables (`WebHost/Program.cs`), so the same image serves preprod and prod — Terraform sets `API_BASE_URL` on each frontend Container App. PR previews use Azure Static Web Apps instead (no image, see `pr-preview.yml`).
- **`HouseFlow.WebHost`** (`src/HouseFlow.WebHost/Dockerfile`, repo-root context) publishes the WASM app *standalone* (only that publish resolves the `index.html` fingerprint placeholders), overlays its `wwwroot` on the published host and serves it on :3000, exposing `/appsettings.json` from the `API_BASE_URL` / `DEMO_MODE` environment variables (`WebHost/Program.cs`). It is no longer deployed to Azure — every environment, production included, serves the frontend from an Azure Static Web App and the `houseflow-frontend` image is not built. The host remains the way the frontend runs under Aspire.
- **Playwright** E2E at repo-root `e2e/` (17 spec files incl. `gdpr-*.spec.ts`, count under *Testing*); run with `bash scripts/verify-e2e.sh` (always restarts the API + frontend; ports/DB overridable — see *Running the Application*).

### Infrastructure

Cible décrite dans `specs/infrastructure.md` (une racine Terraform, deux souscriptions, trois
workflows d'infrastructure) — détail complet là-bas, résumé ici :

- **Terraform** (`infrastructure/terraform/`) — une racine `environment/`, instanciée par
  un `name` et un jeu de variables versionné dans `instances/`, suivie pour chaque instance de
  `dns/` (enregistrements OVH, seule racine sous le verrou `ovh-dns-zone`) puis de
  `custom-domains/` (liaisons de domaine Azure) — trois states par instance, voir #238. Deux instances seulement :
  `prod.tfvars` (sept réglages, dont l'allow-list `preserved_emails`) et `pr.tfvars` (deux :
  `bastion_enabled`, `demo_mode`) — tout le reste est commun. `shared/` ne garde que le Key Vault,
  `id-houseflow-cert`, `id-houseflow-dumps-writer` et le conteneur `db-dumps` ; `modules/ovh-dns-zone/` pose les enregistrements (appelé par `dns/`). Les racines `env-*`,
  `deploy-*`, l'ancienne stack `dns` centrale et `modules/env` n'existent plus
- **Un environnement possède tout ce dont il dépend** — son resource group, son VNet, son serveur
  PostgreSQL, son CAE, son identité. La production est l'instance dont l'échéance est vide ; tout
  le reste porte un tag `ttl`
- **Le verrou du resource group et le réplica d'API maintenu se déduisent de `expires_at == ""`**
  (locals de `environment/main.tf`) au lieu d'être des variables. En faire des réglages rendait
  représentable l'environnement à la fois éphémère et verrouillé — un resource group promis au
  reaper qu'il ne peut pas détruire, soit la fuite d'argent que tout le design écarte
- **PostgreSQL 16** — un Flexible Server par environnement (`psql-houseflow-<nom>`,
  `B_Standard_B1ms`, 32 Go, accès privé), authentification Entra exclusive. La base
  `houseflow_<nom>` est créée par Terraform, plus par un job SQL
- **Frontend** — Static Web App en SKU Free (`swa-<nom>`), partout y compris en production ;
  l'image `houseflow-frontend` n'est plus construite. Le `wwwroot` publié est téléversé avec le
  jeton de déploiement de la SWA
- **API** — Container App `ca-api-<nom>`, image `ghcr.io/barberouss/houseflow-api`, un réplica
  maintenu en production, scale-to-zero sur les environnements de PR
- **Deux souscriptions Azure** — production d'un côté, environnements jetables de l'autre, même
  tenant. Chacune a son `rg-houseflow-shared`, son storage de states, son `id-houseflow-cert` et
  son identité de dumps (`id-houseflow-dumps-writer` côté production, `id-houseflow-dumps-reader` côté jetable)
- **RBAC** — un seul rôle custom actif, `HouseFlow Deployer`, au scope souscription et sans droit
  d'attribution de rôle ; `HouseFlow Shared Tenant` a été supprimé. Détail :
  `infrastructure/rbac/README.md`
- **GitHub Actions** — 2 app registrations OIDC (`houseflow-github-prod`,
  `houseflow-github-preview`), une par environnement GitHub qui accède à Azure, sans secret Azure
  statique. Les environnements GitHub sont `prod`, `prod-approval` (gate humaine, aucune identité)
  et `preview`
- **Trois workflows d'infrastructure** : `pipeline.yml` (merge `main` → build, shared, certificat,
  plan prod, approbation, apply prod), `pr-preview.yml` (un environnement complet par PR, TTL 4 h
  glissant à chaque événement de la PR), `reaper.yml` (horaire, suppression par tag, sous
  l'environnement `preview`). Il n'y a plus d'environnement de validation nommé ni de création à
  la demande : l'environnement d'une PR étant complet, il éprouve un changement d'infrastructure
  dans la PR qui l'introduit
- **Claude Code routine** fired by `claude-issue.yml` — a cloud session starts on an issue when the `claude` label is added
- **GHCR** for container images (PAT `read:packages` for Azure pull)
- **Bastion Container App** (SSH tunnel, scale-to-zero) for private DB access via DBeaver, en
  production uniquement
- **`dbtools`** (`dbtools/`, image `ghcr.io/barberouss/houseflow-dbtools`, un Container Apps Job
  par environnement) — **`dump`** en prod (cron 02:00 UTC) : copie de la base, `pseudonymize.sql`,
  `verify.sql`, publication de `db-dumps/latest.dump` seulement si aucune donnée personnelle ne
  reste ; **`restore`** dans une PR, lancé par `pr-preview.yml` après chaque apply, qui ne restaure
  qu'une fois par environnement, puis l'API redémarre et applique les migrations de la branche.
  Toute colonne texte ajoutée au modèle doit être classée dans `PseudonymizationTests` (et
  traitée dans `pseudonymize.sql` si elle est personnelle) — le test échoue sinon. Détail :
  `dbtools/README.md`

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
2. **Mirror the change in the frontend client by hand**: the Blazor WebAssembly app (`src/HouseFlow.Web`) has **no project reference** to the backend and does **not** use the generated contracts. Its DTOs (`Api/Dtos.cs`) and calls (`Api/ApiService.cs`) are hand-written: every contract change must be copied there manually (`generate-api.sh` only regenerates the backend).
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
   - `LastMaintenanceDto` / `LastMaintenanceKind` → `HouseFlow.Contracts.LastMaintenance(Kind)`; `DeviceMaintenanceTypeRequestDto` → `HouseFlow.Contracts.CreateMaintenanceTypeRequest` (body embedded in `CreateDeviceRequest.maintenanceType`)

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
- JWT-based authentication (15-min access token in memory + rotating refresh token in an HttpOnly cookie)
- "Remember me" at login: 365-day sliding session; otherwise a browser-session cookie (24 h server-side)
- Refresh-token families per login, reuse detection (stolen cookie ⇒ that family is revoked), 10 sessions max per user
- Registration creates **no house** (since 2026-09-27): the first house is created by onboarding (P05 `/setup/house`, `POST /houses`); a user registering through an invitation link only gets the shared house (`AuthResponse.joinedHouseId`)

- Onboarding after registration: P05 `/setup/house` (first house) → P06 `/setup/devices` (catalogue of 6 device types; each checked one has an editable maintenance name (≤ 100), a frequency 3 mois / 6 mois / 1 an / 2 ans and « Dernier entretien »), both skippable
- Landing page P01 `/{locale}` with a non-interactive dashboard preview fed by `Features/Shared/DemoData.cs` (no API call)

### House Management
- Houses list P08 `/houses` (only entry point for creating a house, modal M1) and house page P09 `/houses/{id}` (breadcrumb, banner in the house colour, devices C4 rows, members M5, edit/delete via ⋯); houses shown as C4 cards (P07, P08)
- Optional address fields (address, zipCode, city)
- Banner colour `colorKey` (6-key palette, rotation per owner, editable by the owner in M1) — no house photo
- Invite members by **email + role** (Owner / CollaboratorRW / CollaboratorRO / Tenant); no email is sent — the owner copies the link; invitations can be re-sent (new token) or cancelled; the invitee accepts or declines on P04 `/invitations/{token}` (works signed out)
- Permissions R5 (single source `Application/Common/HousePermissions.cs`), exposed to the UI as `capabilities`

### Device Management
- Add / edit devices through modal M2 (catalogue `Features/Shared/DeviceCatalog.cs` + « Autre », optional brand/model/installation date; creating from the catalogue also creates its maintenance type in the same call)
- Device page P10 `/devices/{id}`: breadcrumb « Maisons › {maison} › {appareil} », maintenance types (C3 rows), history table + total

### Maintenance Tracking
- Maintenance types (M4): Monthly, Quarterly, Semestrial, Annual, Biennial, Custom « tous les n mois / ans » (`customMonths` 1–120)
- « C'est fait » on each C3 row (one click, undo toast) or « Fait à une autre date… » (M3, date/cost/provider/notes; editable, deletable except by tenants)
- Status R1 (overdue / à faire ≤ 30 j / à jour, Europe/Paris day) and due dates R2 (never null) computed server side; dashboard P07 `/dashboard` lists every task to handle across houses (`GET /dashboard`)

## Database Schema

### Key Entities

**User**
- Id (Guid)
- Email (unique)
- FirstName
- LastName
- PasswordHash (BCrypt)
- Theme, Language
- ConsentGivenAt (DateTime?, RGPD — date d'acceptation des CGU / prise de connaissance de la politique)
- ConsentPolicyVersion (string?, version de la politique acceptée — cf. `GdprPolicy.CurrentPolicyVersion`)
- ProcessingRestrictedAt (DateTime?, RGPD Art. 18 — compte gelé : login/refresh refusés, données intactes; posé/levé manuellement)
- LastLoginAt (DateTime?, indexé — dernière connexion par mot de passe, base de la règle « comptes inactifs 3 ans »)
- IsAdmin (bool, default false — platform administrator, see "Administration")
- CreatedAt

**RefreshToken** (one row per issued token; rotation revokes the old row and links it via `ReplacedByToken`)
- Id, UserId, Token (unique), ExpiresAt, CreatedAt / CreatedByIp
- FamilyId (Guid, indexed) — the chain of tokens issued from one login (one per device/browser)
- RememberMe (bool) — 365-day sliding lifetime + persistent cookie, vs 24 h + session cookie
- RevokedAt / RevokedByIp / ReplacedByToken / ReasonRevoked (`Replaced by new token`, `Revoked by user`, `Reuse detected`)
- UpdatedAt

**RefreshToken** — `Token` stores the **SHA-256 hash** of the cookie value (never the clear value);
rotation with reuse detection (a replayed rotated token revokes the whole family).

**AuditLog** — automatic change trail (see `HouseFlowDbContext.SaveChangesAsync`); never records
`PasswordHash`, `Token`, `ReplacedByToken` or `KeyHash`; anonymised/purged by `DataRetentionJob`.

**House** (direct user ownership, no Organization layer)
- Id (Guid)
- Name (required)
- Address (optional)
- ZipCode (optional)
- City (optional)
- ColorKey (string, required, max 16 — banner colour, one of `Core/HouseColors.Palette`: indigo, orange, green, sky, yellow, pink)
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
- Periodicity (int enum: Annual, Semestrial, Quarterly, Monthly, Custom, **Biennial = 5** — appended)
- CustomMonths (int?, 1–120, preferred for `Custom`) / CustomDays (int?, legacy `Custom`)
- BaselineDueDate (DateTime?, R2 due date while there is no record: creation date for « Plus ancien », creation + 30 d for « Je ne sais pas »; null on legacy rows ⇒ creation + 30 d)
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
- Email (string?, 255 — invitee email, required at creation since 2026-09-27, personal data TR-03; null on legacy rows)
- Role (HouseRole)
- Status (Pending, Accepted, Expired, Revoked, Declined)
- ExpiresAt (7 days from creation, reset by « Renvoyer ») / DeclinedAt (DateTime?)
- HouseId → House
- CreatedByUserId → User
- AcceptedByUserId → User (nullable)

## Design System

Source of truth: **`specs/ux/`** — `README.md` (hi-fi handoff: tokens, components, breakpoints, assets; wins on
conflict), then `ecrans-houseflow.html` (15 pages × 3 widths × 2 themes), `popups-houseflow.html` (section 4: M1–M7,
toast, mobile sheet) and `spec-refonte-v2.html` (behaviour: R1–R7, C1–C8). `docs/design/01-refonte-ux.md` is the
functional summary and lists the implementation arbitrations (§ 6). Everything lives in
`src/HouseFlow.Web/Styles/app.input.css`: hex CSS variables in `@layer base` (`:root` = light, `.dark` = dark) mapped to
Tailwind v4 in `@theme inline`. There is no `tailwind.config.js` and no `globals.css`.

**Colour tokens** (one name = one README token; `bg-*`, `text-*`, `border-*`, `fill-*`, `stroke-*`):
- surfaces/text: `background` #f7f5f1 / dark #121117, `card`, `foreground`, `muted-foreground`, `border` (cards, ring
  track, skeletons), `input` (field and secondary-button border), `divider` (between rows), `surface-soft` (table head,
  modal footer), `input-bg`, `segment` (segmented controls);
- brand: `primary` #6366f1 (both themes; text on it = `primary-foreground` #fff), `primary-text` (links, active tab,
  outline « C'est fait »), `primary-soft` (active tab, ADMIN badge), `primary-hover`, `brand-panel` (P02 panel);
- statuses R1 (text/dot = base, pill background = soft): `late`/`late-soft`, `due`/`due-soft`, `ok`/`ok-soft`,
  `on-due` (text on a `due` fill, nav badge);
- `destructive` (+ `-border`, `-hover`, `-foreground`) for delete buttons; `chip` (pastille on a house banner);
  helpers `accent` (hover/pressed surface), `ring` (= primary); `warning`, `warning-soft`, `warning-border` (C8 banner);
  `toast`, `toast-foreground`, `toast-muted`, `toast-action`, `toast-success` (C5, #18171f in both themes).
- Renamed from the v2 wireframe palette (gone): `primary-strong` → `primary-text`, `overdue*` / `destructive-strong` →
  `late`, `destructive-soft` → `late-soft`, `*-strong` → base colour, `bg-muted` → `bg-segment`, `popover` → `card`.
- **Tints** derived with `color-mix()` from one attribute: device tile `[data-type="heat|wood|air|water|safe|pac|other"]`
  (`.hf-device-tile`; mapping `Device.Type` → tint + Lucide icon in `Components/DeviceVisuals.cs`, legacy « Chaudière
  Fioul » → heat, other legacy types → other/`wrench`); house banner `.hf-house-banner[data-house-color=indigo|orange|
  green|sky|yellow|pink]` (attribute may sit on an ancestor; `.hf-chip` for pastilles on it, `.hf-house-swatch` = solid
  colour for the M1 picker).

**Scales**: radii `rounded-field` 10, `-tile` 12, `-card` 16, `-banner` 18, `-modal` 20; shadows `shadow-segment`,
`-modal`, `-sheet`, `-toast`, `-popover`, `-hero`, `-panel`, `-bar`, `-preview` (cards have **no** shadow, only a 1 px
`border`); widths `max-w-app` 1000, `max-w-reading` 720 (P11, P14/P15), `max-w-form` 520 (auth, setup),
`max-w-login` 400, `max-w-setup` 1160 (P06); breakpoints `sm` 640, `lg` 1024 (R7: three widths).

**Typography**: self-hosted fonts (`wwwroot/fonts/*.woff2`, SIL OFL, latin + latin-ext, `font-display: swap`, 3 preloads
in `index.html`) — **Bricolage Grotesque** 600/700 for headings (`font-display`, `h1`/`h2` automatically), **Instrument
Sans** 400–700 for text/UI (`font-sans`). Never loaded from Google Fonts (RGPD: no visitor IP sent to a third party).
Utilities: `hf-hero` (P01, 38/52/56), `hf-h1` (app pages, 30/38/44), `hf-h1-form` (auth/setup, 28/32/34), `hf-h2` (22),
`hf-section-label` (13/700 uppercase; + `text-late`/`text-due`), `hf-meta`; page gutter `hf-gutter` (20/32/40 px).

**`hf-*` building blocks** (plain CSS in `@layer components`, never used behind a variant or `@apply`):
- buttons `hf-btn` + `-primary|-outline|-danger|-ghost|-danger-ghost|-muted` (« Passer »)`|-link|-hero`; sizes
  `hf-btn-sm`, `hf-btn-block` (≈ 50 px full width), `hf-btn-icon` — all ≥ 44 px;
- fields `hf-label` (+ `<span class="hf-optional">· facultatif</span>`), `hf-input` (50 px, 16 px text, focus = 2 px
  `primary` border, `aria-invalid="true"` for errors, chevron on `select.hf-input`), `hf-input-compact` (44 px: P06, M5),
  `hf-help`, `hf-field-error`, `hf-checkbox`, `hf-segmented` / `hf-segment` (`role="radio"` + `aria-checked`), `hf-link`;
- surfaces `hf-card`, `hf-box` + `hf-row` (framed lists, 14 × 16 padding, `divider`), `hf-group-label`, `hf-alert-error`;
- status pill `<span class="hf-pill" data-status="late|due|ok|none"><span class="hf-dot"></span>…</span>`;
- `hf-skeleton` (`border` fill, 1 → 0.5 opacity pulse over 1.2 s, off with `prefers-reduced-motion`), `hf-tabbar-space`
  (room for the mobile tab bar), `hf-sr-until-focus`.

**App icon** (`specs/ux/assets` → `wwwroot/icons/icon-{ok,due,late,none}.svg` for the header logo ≥ 32 px,
`favicon-*.svg` for `<link rel="icon" id="hf-favicon">`; C2PA metadata stripped): house-calendar tile whose calendar
cell takes the global status colour. `ok` is also the fixed icon (public pages, PWA: `icon-192.png`, `icon-512.png`,
`manifest.webmanifest` with `background_color` #f7f5f1 / `theme_color` #6366f1). See `AppIconService` below.

**Rules** (enforced in review): tokens only in `Features/`, `Layout/`, `Components/` — no raw hex, no `blue-*`/`gray-*`/
`amber-*`…; no gradients, no translucent surfaces or `/50` hovers (solid `bg-accent`); text contrast ≥ 4.5:1 in both
themes; touch targets ≥ 44 px; inputs ≥ 16 px under 640 px (no iOS zoom).

**Tailwind v4 pitfalls** (see Recent Changes « Tailwind CSS v3 → v4 »): classes composed in C# are scanned
(`@source` covers `Components/`, `Features/`, `Localization/**/*.cs`); `text-xs`…`text-4xl` line-heights are pinned to
the v3 absolute values; `hidden` now sorts before `inline-flex` — on a component that already sets a display, hide with
`max-sm:hidden`, not `hidden sm:inline-flex`. Icons: `BlazorBlueprint.Icons.Lucide`.

## Internationalization (i18n)

**Languages**: French (fr, default) and English (en). Locale = first URL segment (`/fr/dashboard`, `/en/dashboard`);
`<html lang>` follows it (`index.html` at boot, then `Components/LocaleBoundary.razor` → `hf.setLang`). A known route
typed without locale (`/dashboard`, `/login`, `/houses/…`, `/invitations/…`…) is redirected to `/fr/…` with its query
string (`AppRoutes.LocalelessRedirect`, from `NotFoundPage`); an unknown single segment (`/foo`) is a 404 (P13).

**Catalogs** (embedded): `src/HouseFlow.Web/Localization/Resources/{fr,en}.json` — nested namespaces resolved as dotted keys
by `Localization/Localizer.cs` (`{var}` substitution, ICU `{count, plural, one {…} other {…}}`, `{count}` allowed inside a
plural branch). Components inherit `Components/AppComponentBase` and call `T("ns.key", new { … })`; the base class
re-renders on locale change and offers `FormatDate`, `FormatMonthYear`, `RelativeDate`, `FormatMoney`.
```razor
@inherits AppComponentBase
<h1>@T("dashboard.allUpToDate")</h1>
```

**Conventions**
- Every new string goes in **both** `fr.json` and `en.json` (same key set); no hard-coded UI text, including
  placeholders (`auth.*Placeholder`) and toast/aria labels.
- English copy: sentence case (« First name »), « Sign in », and « house » everywhere (never « home »).
- **Frozen texts** — never reworded without the legal procedure (`CLAUDE.md` RGPD section): `legal.*`, `consent.*`,
  `invitations.privacyNotice*`, `footer.*`, the `Features/Legal/*Content*` components, and the FR values selected by the
  frozen `gdpr-*.spec.ts` E2E specs.
- Namespaces: `common`, `auth`, `landing`, `dashboard`, `houses`, `devices`, `devicePage`, `catalog`, `maintenance`,
  `lastMaintenance`, `recordModal`, `typeModal`, `confirmDelete`, `members`, `invitations`, `setup`, `stepper`, `settings`,
  `account`, `apiKeys`, `admin`, `header`, `nav`, `status`, `dates`, `score`, `toast`, `async`, `confirm`, `avatars`,
  `errors`, `legal`, `consent`, `footer`.

## Dark Mode

Managed by `ThemeService` (`src/HouseFlow.Web/ThemeService.cs`), which applies the `dark`/`light` class on `<html>` (via
`hf.applyTheme` in `wwwroot/js/app.js`) and persists the choice in `localStorage` (`houseflow_theme`). Since the redesign the
theme (light / dark / system, `Components/ThemeToggle.razor`) and the language (`Components/LocaleSwitcher.razor`) are chosen
in **P11 `/settings#preferences`**, no longer in the header. Options « Clair / Sombre / Système », **system by
default**. An inline script in `index.html` applies the stored theme (and the `theme-color` meta, #121117 / #f7f5f1)
before Blazor boots — no flash. Every token has a dark variant (`.dark` block of `app.input.css`, full dark mode per
`specs/ux`); `color-scheme` follows the theme.

## Loading UX

- **`Components/AsyncSection.razor` (C6)** is the standard way to load a page section: `Load` (Func<Task<T>>), `Loading`
  (skeleton), `Empty`/`IsEmpty`, `ChildContent` (Context = value), `Error(ex, retry)`, `HandleNotFound` (404/403
  `ApiException` → inline `ErrorPage` P13). `ReloadAsync()` keeps the previous value while reloading and surfaces a failed
  reload as a non-intrusive error with « Réessayer ». Pattern after a mutation:
  `await _section.ReloadAsync(); await NavCounter.RefreshAsync();`
- Skeletons: `Skeleton` (Width/Height/Circle), `SkeletonRows` (Count, Boxed, ShowTrailing, ShowTile), `SkeletonHeader` (ShowRing, RingSize), `SkeletonHouseCards` (Count),
  `ProgressRing` renders a skeleton while its counts are null — never « Loading… » text.
- `BusyLabel` for buttons during a submit; modals catch `HttpRequestException` / `TaskCanceledException` and show an error
  banner or toast (never the Blazor error screen).
- **Retry indicator**: `Components/RetryIndicator.razor` (backed by `Api/RetryState.cs`) shows a "reconnecting" banner while
  a transient request is being retried.

## Frontend Architecture (UX redesign 2026-09-27, final visual design 2026-09-29)

Spec: **`specs/ux/`** (see *Design System* for the priority order: `README.md` > `ecrans-houseflow.html` >
`popups-houseflow.html` > `spec-refonte-v2.html`) + `docs/design/01-refonte-ux.md` (functional summary of P01–P15,
M1–M7, C1–C8, R1–R7 and the implementation arbitrations). Out of scope: email reminders (separate issue).

### Routes (all `/{locale}/…`)
| Page | Route | Component | Layout |
|---|---|---|---|
| P01 Présentation | `/`, `/{locale}` (signed in → P07) | `Features/Shared/Landing.razor` | `ErrorLayout` (public for a guest) |
| P02 Connexion | `/login?returnUrl=&reason=restricted` | `Features/Auth/Login.razor` | `AuthLayout` |
| P03 Inscription | `/register?returnUrl=&invitation=` | `Features/Auth/Register.razor` | `AuthLayout` |
| P04 Invitation | `/invitations/{token}[?accept=1]` (works signed out) | `Features/Invitations/AcceptInvitation.razor` | `MainLayout` |
| P05 Setup · maison | `/setup/house` *(new)* | `Features/Setup/SetupHouse.razor` | `SetupLayout` |
| P06 Setup · équipements | `/setup/devices` *(new)* | `Features/Setup/SetupDevices.razor` | `SetupLayout` |
| P07 Accueil | `/dashboard` | `Features/Dashboard/Dashboard.razor` | `DashboardLayout` |
| P08 Maisons | `/houses` | `Features/Houses/HousesPage.razor` | `DashboardLayout` |
| P09 Maison | `/houses/{id}` | `Features/Houses/HouseDetailPage.razor` | `DashboardLayout` |
| P10 Appareil | `/devices/{id}` | `Features/Devices/DeviceDetailPage.razor` | `DashboardLayout` |
| P11 Compte | `/settings` (`#profil #preferences #donnees #api #suppression`) | `Features/Settings/Settings.razor` + `ProfileSection`, `DataSection`, `ApiKeysSection`, `DeleteAccountSection` | `DashboardLayout` |
| P12 Administration | `/admin` (non-admin → P13 403) | `Features/Admin/AdminPage.razor` | `DashboardLayout` |
| P13 Erreurs 404/403 | any unknown route; 404/403 API answers | `Features/Shared/NotFoundPage.razor`, `Components/ErrorPage.razor` | `ErrorLayout` |
| P14 Confidentialité | `/privacy` | `Features/Legal/PrivacyPolicy.razor` (via `LegalPage`) | `MainLayout` |
| P15 CGU | `/terms` | `Features/Legal/TermsOfService.razor` (via `LegalPage`) | `MainLayout` |

**Removed routes**: `/houses/new` (house creation = modal M1 on P08, or P05 during onboarding) and
`/houses/{id}/devices/new` (device creation = modal M2 on P09, or P06). `NewHouse.razor`, `NewDevice.razor`,
`AddMaintenanceTypeDialog`, `LogMaintenanceDialog`, `MembersSection`, `StatCard`, `ScoreRing`, `HouseRow`,
`HouseSelector`, `StatusDot` are gone (`Breadcrumb` was rewritten for `specs/ux`).

**Flows** — register → P05 → P06 → P07 (each « Passer » skips); register/login from an invitation →
`AuthResponse.joinedHouseId` → P09; P04 « J'ai déjà un compte » → `/login?returnUrl=/{loc}/invitations/{t}?accept=1`
(auto-accept after login); P03 reads the invitation token from such a returnUrl (`AppRoutes.InvitationTokenOf`) and locks the
email. Logout → P01. Account deletion (M7) → P02 (history replaced). Restricted account on refresh → P02
`?reason=restricted`. R6: an app page without session → `/login?returnUrl=<page>`; a signed-in user on `/login` goes to
the (safe) returnUrl or P07; a signed-in user on `/register?invitation=…` goes to P04.

**Modals**: M1 `Features/Houses/HouseModal.razor`, M2 `Features/Devices/DeviceModal.razor`, M3
`Components/MaintenanceRecordModal.razor`, M4 `Components/MaintenanceTypeModal.razor`, M5
`Features/Houses/MembersModal.razor`, M6 `Components/ConfirmDialog.razor`, M7 in `DeleteAccountSection.razor`.

### Layouts (`src/HouseFlow.Web/Layout/`)
- `MainLayout` — public layout (C7 footer + toasts, no header/banner): P01, P04, P14, P15; parent of `AuthLayout` and
  `SetupLayout`.
- `AuthLayout` — guest-only pages P02/P03 (a signed-in user is redirected).
- `SetupLayout` (`ProtectedLayoutBase`) — session required, no app chrome, stepper « Compte · Maison · Équipements »: P05/P06.
- `DashboardLayout` (`ProtectedLayoutBase`) → `AppShell` — P07–P12.
- `AppShell` — signed-in chrome: C1 `Header` (≥ 640 px), C8 `ConsentBanner` under it, content, C7 `Footer`, C2 `TabBar`
  (< 640 px), C5 `ToastHost`.
- `ErrorLayout` — P13 (and P01): `AppShell` when a session is active, public otherwise.
- `ProtectedLayoutBase.cs` — R6 redirect to `/login?returnUrl=` (history replaced), re-checked on auth-state change.

### Shared components (`src/HouseFlow.Web/Components/`)
- `AppComponentBase` — `T()`, locale, date/money helpers, re-render on locale change.
- `AsyncSection<TItem>` (C6) — loading / empty / error / 404-403 states of a section (see *Loading UX*).
- `Header` (C1: 64 px, logo + Accueil/Maisons + avatar menu; public variant 68 px with « Se connecter » / « Créer un
  compte »; logo only at 56 px under 640), `TabBar` (C2, 64 px), `NavBadge` (count of tasks to handle, `data-variant`
  late|due = global status, `Small`), `Footer` (C7, `Compact` = P02 variant), `ConsentBanner` (C8, frozen texts),
  `ToastHost` (C5, above modals, `ToastMessage.Detail`/`Success`), `Logo` (`Size`, `Status` — app icon by status +
  « HouseFlow »), `AppIconScope` (claims the app icon for a layout: fixed `Status`, or none = follow the global status),
  `LocaleBoundary` (`<html lang>`), `RetryIndicator`.
- `MaintenanceRow` (C3: type tile 44 on P07 / status dot on P10, due date, « C'est fait » always with its label, ⋯ not
  rendered under 640 px on P07), `MaintenanceRowItem.cs` (its view model, incl. `DeviceType`),
  `MaintenanceActions` (hosts M3/M4/M6 + « C'est fait » / undo for a list of rows), `MaintenanceRecordModal` (M3),
  `MaintenanceTypeModal` (M4), `LastMaintenancePicker` + `LastMaintenanceChoice.cs` (« Dernier entretien » : month+year /
  « Plus ancien » / « Je ne sais pas »).
- `HouseCard` (C4 card, P07 « Mes maisons » + P08 grid `grid gap-4 sm:grid-cols-2 lg:grid-cols-3`: 104 px banner in
  the house colour, one `DeviceTile` chip per device up to `MaxChips` = 5 then « +n », « Partagée » badge when
  `UserRole ≠ Owner`, name/city/device count, status pill, fraction; `HouseCard.Sort` = status then name; testids
  `house-card`, `house-card-link`, `house-card-subtitle`, `house-card-fraction`, `house-shared`), `SkeletonHouseCards`,
  `DeviceRow` (C4 device row on P09: tile 46, brand · model, pill, fraction, chevron), `DeviceTile` (`Type`, `Size`
  34–72, `IconOverride` — tint + Lucide icon from `DeviceVisuals.cs`), `Breadcrumb` (`Items` = `Breadcrumb.Crumb(Label,
  Href)`, `Current`; « ‹ Parent » under 640; testids `breadcrumb` / `breadcrumb-back`), `DashboardHeader` (P07 greeting,
  title, « dont r en retard », score card; the P01 preview has its own miniature `Features/Shared/LandingPreview`).
- `StatusBadge` (status pill by default, `Plain` text variant; `data-status` late|due|ok|none), `ProgressRing` (default
  64 px, P09 56, preview 104; up-to-date / total; hidden when total = 0).
- `Modal` (legacy mode or form mode with `Title`: primary/cancel, `Busy`, `Destructive`, `FooterStart`, `InitialFocus`;
  bottom sheet < 640 px; closes on backdrop only when mousedown and click are both on it), `ConfirmDialog` (M6, focus on
  « Annuler »), `MenuButton` / `MenuItem` / `OverflowMenu` (⋯ menus; long-press on mobile, ⋯ kept reachable by keyboard).
- `Avatar` (pastel colour per name, `Pending` = dashed border), `AvatarStack` (34 px), `Stepper` (3 bars; labels sr-only
  under 640, where the page shows « Étape n sur 3 » next to the logo), `BusyLabel`, `Skeleton`, `SkeletonRows`
  (`ShowTile`), `SkeletonHeader` (`RingSize`), `ErrorPage` (P13 body with the tilted-house illustration, `Code`
  404|403), `ThemeToggle` / `LocaleSwitcher` (segmented radio groups).

### Services (`src/HouseFlow.Web/Services/`, scoped)
- `ToastService` — single toast (6 s, held on hover/focus): `Show(text, params ToastAction[])`, `ShowError`,
  `Show(ToastMessage)`, `Dismiss()`; `ToastAction(Label, Func<Task>, TestId?)` for « Annuler » / « Réessayer ».
- `NavCounterService` — `ToProcess` / `Overdue` / `Total` (null until loaded) from `GET /dashboard`; `Status`
  (`AppStatus` None/Ok/Due/Late: overdue > 0 → Late, else to-process > 0 → Due, else total > 0 → Ok, else None);
  `EnsureLoadedAsync`, `RefreshAsync` (call after every mutation that changes a status), `Set(toProcess, overdue,
  total)`, `SetFrom(dashboard)`, `Reset`, `OnChange`.
- `AppIconService` — which app icon is shown (header logo + favicon via `hf.setAppIcon`): the most recent
  `AppIconScope` wins — the public layouts (`MainLayout`, `AuthLayout`, `ErrorLayout` signed out) fix `Ok`, onboarding
  (`SetupLayout`) fixes `None`, the signed-in app (`AppShell`) follows `NavCounterService.Status`. `index.html` sets the boot favicon with the same rule.
- `SessionService` — the single logout path (header/P11 menu → P01, account deletion → P02, restricted refresh → P02
  `?reason=restricted`): revokes the session, clears tokens/counters/toasts, arms `RedirectGuard`.
- Routing helpers in `Auth/AppRoutes.cs`: `CurrentLocale`, `LoginUrl`, `SafeReturnUrl` (same-origin relative paths only),
  `AfterLogin`, `RestrictedLoginUrl`, `InvitationTokenOf`, `LocalelessRedirect`, `SectionOf` (active nav item).
- JS interop (`wwwroot/js/app.js`): `hf.modal` (focus trap / scroll lock), `hf.menu`, `hf.sections` (P11 anchors),
  `hf.legal`, `hf.setLang`, `hf.applyTheme`, `hf.setAppIcon`, `hf.downloadFile`.

### Rules (`src/HouseFlow.Web/Rules/`, plain C#, unit-tested in `tests/HouseFlow.UnitTests/Web/`)
The server is the source of truth for statuses (R1) and due dates (R2); these mirror it for display and previews.
- `ParisClock` — Europe/Paris "today" (`Today`, `ToParisDate`, `NowOverride` for tests).
- `StatusRules` (in `DueStatus.cs`) — `Compute(date|string)`, `FromApi(status)` (`overdue` / `pending` / `up_to_date` /
  `none`), `MostUrgent`, `LabelKey`, `DueSoonWindowDays = 30`.
- `DateFormatter` (in `DueStatus.cs`) — `Absolute`, `MonthYear`, `Relative` (R4: « en retard de N j » for any overdue item,
  relative up to 60 days in the future, « mars 2027 » beyond), `ParseDate`; `MoneyFormatter.Euros`.
- `PeriodicityRules` — `Months(periodicity, customMonths)`, `NextDue`, `Words` (i18n label « Tous les n mois / ans »).
- `InitialDueRules` — `FirstDue` / `PreviewLabel`: due-date preview of M2/M4/P06 from the « Dernier entretien » choice
  (R4: relative up to 60 days, « Octobre 2026 » beyond, capitalised).
- `Features/Shared/DeviceCatalog.cs` — the 6 catalogue device types with their default maintenance type and periodicity;
  `Components/DeviceVisuals.cs` — `Device.Type` → tint key + Lucide icon.

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

**Sessions (issue #164)** — `AuthService` / `AuthController`, constants on `AuthService`:
- Login with `rememberMe: true` → refresh token and cookie valid **365 days**, renewed on every refresh
  (sliding). Without it → **24 h** server-side and a **session cookie** (no `Expires`), gone when the browser closes.
  Registration always opens a plain (non-remembered) session.
- Each login opens a **family** (`FamilyId`); `/auth/refresh` rotates the token inside the family.
- **Reuse detection**: presenting an already-rotated token means two parties hold it. Within a **30 s grace
  period** (two tabs booting with the same cookie) the current token is simply re-issued; beyond it the whole
  family is revoked (`ReasonRevoked = "Reuse detected"`) and the caller gets 401 — other devices are untouched.
- **10 sessions max** per user: a new login evicts the least recently used family (an active token's
  `CreatedAt` is its last rotation). Revoked/expired tokens are pruned after 7 days (kept for detection).
- Frontend keeps the access token **in memory only**; the session survives reloads/new tabs/browser restarts
  through the cookie exchanged at boot. The boot refresh only runs when a **session hint** (`localStorage`
  `houseflow_session` = "1", set on login/register/refresh, removed on logout or when the server rejects the cookie)
  is present: a logged-out visitor gets the login page without any API round-trip (the API of an ephemeral
  environment scales to zero and cold-starts in ~30 s). While restoring, `App.razor` shows the same splash as
  `index.html` (never a blank page) and gives up after 45 s (hint kept, app starts logged out).
- Cookie attributes: `HttpOnly`, `Path=/`, `Secure` behind HTTPS, `SameSite` from `Auth:CookieSameSite` (**Lax** by default:
  CSRF protection on `/auth/refresh` and `/auth/logout`). `None` (forces `Secure`) is set only where the frontend and the API
  are on different sites: the local/CI E2E API (`scripts/dev-api.sh`, `pr.yml`), whose suite drives the frontend from
  `http://127.0.0.1:3000` against `http://localhost:5203` to reproduce that cross-site case (`session-persistence.spec.ts`).
  Prod and the PR previews (`pr-<n>` / `api-pr-<n>.houseflow.cloud` since #203) are same-site and keep Lax.
- Not yet: revoking every session on password change (there is no password-change endpoint yet).

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
# (Tailwind CSS is compiled by `dotnet build src/HouseFlow.Web`; `npm run watch:css` for a live watch)
```

### Testing

**Backend Tests**:
```bash
dotnet test
```

**Frontend E2E Tests** (Playwright, suites at repo-root `e2e/`):
```bash
bash scripts/verify-e2e.sh   # (re)starts the API + Blazor frontend, then runs all scenarios
# Several worktrees side by side on one machine (no devcontainer): one port set + one DB each
POSTGRES_HOST=localhost API_PORT=5301 WEB_PORT=3301 DB_NAME=houseflow_a bash scripts/verify-e2e.sh
```
`verify-e2e.sh` exports `FRONTEND_URL` / `API_URL` to Playwright and passes `CORS__ORIGINS` for the
chosen `WEB_PORT`. It always restarts both servers: a dev server started before a `dotnet build`
serves a stale `_framework` manifest (404 on `dotnet.<hash>.js`) and the WASM app never boots.

**E2E fixtures & page objects** (`e2e/`):
- `fixtures/auth.ts` — `test` fixture with `authenticatedPage` (registers through P03, creates « Ma maison » on P05, skips
  P06 and lands on the empty P09 house page), `registerViaApi` (isolated `request` context; creates no house), `createHouseViaApi`, `createInvitationViaApi`, `generateTestEmail`,
  `refreshCookieFrom`, `addRefreshCookie`, `SESSION_HINT_KEY`.
- `fixtures/maintenance-seed.ts` — API seeding for maintenance scenarios: `registerUser`, `createHouse`, `createDevice`,
  `createType` (with `lastMaintenance`, e.g. `monthsAgo(n)`), `logRecord`, `isoDaysAgo`, `openAs(page, session, path)`.
- `fixtures/db.ts` — direct SQL through `psql` for states no endpoint produces (`restrictAccount(userId)`, RGPD Art. 18);
  `E2E_DB_HOST` / `E2E_DB_NAME` overrides.
- Page objects: `pages/login-page.ts`, `register-page.ts`, `setup-page.ts` (P05/P06), `house-page.ts` (P09 + M2
  `addDevice` with catalogue id and « Dernier entretien »), `settings-page.ts`. Selectors use `data-testid` / roles, never
  Tailwind classes. The frozen `gdpr-*.spec.ts` must keep passing **without being edited** (only the post-registration URL
  expectation of `gdpr-consent-legal.spec.ts` was changed to `/fr/setup/house`).
- Specs added by the redesign: `landing.spec.ts` (P01), `dashboard.spec.ts` (P07), `restricted-account.spec.ts`.

**Devcontainer gotchas** (worktree container, see `.devcontainer/README.md`):
- Heavy commands (build, test, E2E, dev-server restart) from several agents/shells in the same container must be
  serialised: `flock -o /tmp/hf-heavy.lock <cmd>` — `-o` so that MSBuild/VBCSCompiler node servers do not inherit the lock
  fd (without it the lock is held until those servers exit). A stuck build: `dotnet build-server shutdown`.
- Playwright's Chromium is not in the image: `cd e2e && npx playwright install chromium` once per container.
- After `docker compose` restarts, root-owned `obj/**/*.Up2Date|*.cache` files can break MSBuild (MSB3374) — delete them.
  `dotnet tool restore` is needed again for NSwag in a fresh container.
- Never build the same projects from the Windows host and the container (`obj/project.assets.json` flips paths).
- Host-browser access works out of the box, at the same time as in-container E2E (2026-09-29):
  `feature-env.sh up|url` writes the published ports to `/tmp/hf-host-ports.env` in the container;
  `dev-web.sh` serves the frontend through `HouseFlow.WebHost`, whose `/appsettings.json` returns the host API port to
  requests that came through the published web port (`HOST_WEB_PORT`/`HOST_API_PORT`) and `:5203` to the others;
  `dev-api.sh` adds the host web origin to CORS. Open the « Frontend » URL of `feature-env.sh url <name>`.
  WebHost only serves fingerprinted `_framework` names (import map) — probe the `<script src>` targets, not `dotnet.js`.
  `feature-env.sh exec` now runs in `/workspace`, adds `-T` without a terminal and disables Git Bash path mangling.

**Current Test Status** (verified 2026-09-27, devcontainer):
- Backend: 464 tests passing (194 unit + 270 integration)
- E2E: 107 Playwright scenarios passing (chromium, 17 spec files, CI profile 2 workers: ~6 min)
- `dotnet build src/HouseFlow.Web`: 0 warnings, 0 errors

## RGPD / Data Protection (2026-09-11)

Compliance dossier: `docs/gdpr/` (register Art. 30, LIA, retention policy, subprocessors, rights-requests
log) + `docs/security/breach-notification-procedure.md` / `breach-register.md`. Issues GitHub #132 à #139 (une par obligation), fermées par la PR #163.
Legal reference used for the implementation: primary sources (GDPR text, CNIL, EDPB, APD).

**Legal basis** — account/houses/devices/maintenance = contract (Art. 6(1)(b)); security/audit/refresh
tokens/invitations = legitimate interest (Art. 6(1)(f), LIA documented). The registration checkbox is an
acceptance of the **Terms of Service** (contract) plus a *notice* of the Privacy Policy (Art. 13) — it is NOT
an Art. 7 consent (EDPB Guidelines 05/2020: no bundling, no fictitious consent). Technical names keep the
issues' wording (`consentAccepted`, `ConsentGivenAt`, `ConsentPolicyVersion`). No third-party trackers:
the three cookies (`refreshToken` HttpOnly `Path=/api/v1/auth`, `houseflow_session`, `houseflow_theme`) are strictly necessary → no cookie
banner (art. 82 LIL), documented in the policy.

**Endpoints** (`specs/openapi.yaml`, section *USER ACCOUNT & RGPD*, all `[Authorize]`):
| Endpoint | Article | Notes |
|---|---|---|
| `GET /api/v1/users/me` | 15 | profile + `consentRequired` |
| `PUT /api/v1/users/me` | 16 | rectification (firstName/lastName/email, 409 if email taken) |
| `DELETE /api/v1/users/me` | 17 | body `{password}`; immediate hard delete; owned houses transferred to the oldest collaborator (RW then RO) else deleted with content; memberships removed; refresh tokens + API keys deleted (cookie cleared); audit logs anonymised (`UserId` null, `Username` = `deleted-user`, IP/UA/values null) + `AccountDeleted` trace (account UUID replaced by `deleted`); 204 |
| `GET /api/v1/users/me/export?format=json\|csv` | 15 + 20 | JSON document or ZIP of CSVs + README; includes an `information` section (Art. 15(1)(a)-(h)); never secrets nor third-party identities; 1 export/hour (`429` + `Retry-After`), audit `DataExport` |
| `GET/POST /api/v1/users/me/consent` | 7 / 5(2) | status / (re)acceptance of the current policy version |
| `POST /api/v1/auth/register` | 6(1)(b), 13 | `consentAccepted` must be `true` (400 otherwise); `ConsentGivenAt` + version stored, IP in the audit trail |

**Retention** (`DataRetentionJob`, Hangfire daily 03:00 UTC, `DataRetention` section of `appsettings.json`,
batched `ExecuteUpdate/Delete`, idempotent, one log line per rule): IP truncation after 30 days
(`IpAddressAnonymizer`: IPv4 last octet / IPv6 last 80 bits) on audit logs, refresh tokens, API keys;
revoked/expired refresh tokens and revoked API keys purged after 30 days; audit logs anonymised after 1 year,
deleted after 3 years; soft-deleted entities after 30 days; expired invitations after 30 days (former
`CleanupExpiredInvitationsJob`, merged). Inactive accounts (3 years): manual procedure documented.

**Security (Art. 32)** — password policy 8 chars + lower/upper/digit/special (4 of 4; the CNIL 2022 recommendation allows 8 when an attempt-limiting mechanism protects the account — 5 req/min/IP on the auth routes, see `SECURITY.md`); refresh tokens hashed; CSV export neutralises spreadsheet formulas (CSV injection); CI job `dependency-audit` (`dotnet list package --vulnerable` + `npm audit`, fails on High/Critical in direct packages of deployed projects) + Dependabot weekly;
`dotnet HouseFlow.API.dll --revoke-all-sessions` kill-switch (breach procedure); application logs contain no
email/IP/token; prod data leaving production is pseudonymised and verified by `dbtools/` (register entry TR-07).

**Frontend** — `Features/Legal/` (`/{locale}/privacy`, `/{locale}/terms`, FR + EN content components,
`LegalConstants.PolicyVersion` must equal `GdprPolicy.CurrentPolicyVersion`), `Components/Footer.razor`
(all layouts), `Components/ConsentBanner.razor` (non-blocking re-acceptance banner on the dashboard),
`Features/Settings/Settings.razor` sections *Profil* / *Mes données* (JSON/CSV download via
`hf.downloadFile`) / *Supprimer mon compte* (checkbox + password modal), i18n namespaces `account`, `legal`,
`consent`, `footer`. E2E: `e2e/tests/gdpr-account.spec.ts`, `e2e/tests/gdpr-consent-legal.spec.ts`.

**Maintenance rule** (see `CLAUDE.md`): any new personal data, purpose, recipient/subprocessor or retention
period must update the register, the privacy policy (+ bump both policy-version constants), the retention
policy/job and `docs/gdpr/subprocessors.md` in the same PR. Decided: controller = Rouss Consulting SRL (BE0805984579), contact `privacy@houseflow.cloud` (no postal
address while the service is free), lead supervisory authority = APD (Belgium), Belgian law with the Rome I
Art. 6 reservation. Human actions still open: Microsoft DPA version/acceptance date, annual DPF
certification check, legal review of the policy/terms texts, backup-restore test, breach simulation
exercise, and — before any sale — a geographic address plus CGV/withdrawal/payment processor/7-year
accounting retention (`docs/gdpr/README.md` § 7).

## Recent Changes (2026-09-29) — Final visual design `specs/ux` applied (+ house colour `House.colorKey`)

**Fixes found while finishing the design (2026-09-29)**
- **Session loss on rapid reloads (auth bug).** The refresh token rotates on every refresh; a response lost to a
  reload/tab close left the browser with the previous token, and the single 30 s grace per token meant two lost
  responses in a row were treated as theft → family revoked → user logged out. `AuthService.RefreshTokenAsync` now
  derives the grace replacement deterministically (HMAC-SHA512 under a key derived from `Jwt:Key`): any repeat of the
  old token within the window gets the **same** replacement while it is unused; once that replacement has been used,
  replaying the old token still triggers theft detection. Concurrent races on the unique token index are handled.
  No migration. Tests: 2 unit, 1 integration, E2E `session-persistence` « Refresh responses lost to reloads… ».
- **Dates:** month + year uses the full month name everywhere (« octobre 2026 »); P06/M2/M4 previews follow R4
  (relative within 60 days). One capitalisation helper: `DateFormatter.Capitalize`, `AppComponentBase.DueLabel()`.
- **C1 header:** avatar + first name from 640 px (`user-first-name`); the menu button's name includes the first name.
- **M5:** owner-only tenant rights checkboxes restored (« Enregistrer un entretien », « Voir les coûts »:
  `member-permissions`, `member-can-log`, `member-can-view-costs`), hidden in the RW restricted mode.
- **P09 DeviceRow:** subtitle = brand · model, else the device type; the status pill carries the urgency.
- **P06:** « Aucun entretien » when nothing is selected.
- `HouseRoleLabels` lives in `Features/Houses` — `Web/Rules` must stay free of any `Api` dependency (compiled by link
  into the unit tests).
- E2E: 116 tests; the Lucide icon test polls (the icon set loads after first render).

`specs/ux/` (hi-fi handoff: `README.md`, `ecrans-houseflow.html`, `popups-houseflow.html`, `spec-refonte-v2.html`,
`assets/`) replaces the old per-screen mockups (`dashboard.html`, `house.html`, `device.html`, `login.html`… deleted)
and is now the **single UX source of truth**; the duplicate `docs/design/spec-refonte-v2.html` was removed (identical to
`specs/ux/spec-refonte-v2.html` but for whitespace) and `docs/design/01-refonte-ux.md` now points to `specs/ux`, carries
the design's behaviour changes and lists the implementation arbitrations (§ 6). `specs/requirements.md` and
`specs/architecture.md` (new « Frontend : design system » section) were aligned on the delivered product.

### Frontend
- **Foundations** (see *Design System*): new light/dark token set (warm beige background, primary #6366f1, R1 statuses
  `late`/`due`/`ok`), radii/shadows/content-width scales, three widths (640 / 1024), typography utilities, `hf-*`
  primitives restyled (50 px fields with 2 px focus border, segmented controls, pills). Fonts **self-hosted**
  (`wwwroot/fonts`, Bricolage Grotesque + Instrument Sans, OFL) — the README said Google Fonts, rejected for RGPD (no
  visitor IP sent to Google). Full dark mode; theme option « Automatique » renamed « Système » (default).
- **App icon by global status**: `Logo` + favicon (`wwwroot/icons/{icon,favicon}-{ok,due,late,none}.svg`, from
  `specs/ux/assets`, C2PA metadata stripped) driven by `AppIconService` / `AppIconScope` and
  `NavCounterService.Status`; `ok` fixed on public pages, `none` during onboarding and before data. PWA manifest
  `manifest.webmanifest` + `icon-192.png` / `icon-512.png`. Nav badge coloured `late` if ≥ 1 overdue, else `due`.
- **Components**: C1–C8 restyled; new `HouseCard` (replaces `HouseRow`), `SkeletonHouseCards`, `DeviceTile` +
  `DeviceVisuals.cs` (type tint + icon wherever a device appears), `Breadcrumb` (replaces `HouseSelector` on P09/P10);
  `StatusDot` removed (`StatusBadge` pill / `Plain`, `hf-pill`). « C'est fait » always keeps its label; the ⋯ menu is
  not rendered under 640 px on P07 (the mobile toast offers « Ajouter des détails »); toast #18171f in both themes.
- **Pages / modals**: P01–P15 and M1–M7 per `ecrans-houseflow.html` / `popups-houseflow.html` (3 widths × 2 themes).
  Notable behaviour changes: P01 preview in a browser window with the mockup's `DemoData` (Marc, 2 houses, 3 tasks,
  8/11); P05 tile in the colour the house will get (`nextColorKey`); **P06 three fields per checked device** (editable
  name ≤ 100 reverting to the default on blur, frequency 3 mois / 6 mois / 1 an / 2 ans, « Dernier entretien »), live
  schedule right ≥ 1024 / below 640–1023 / fixed bottom bar < 640; P07/P08 house cards (+ « Tout voir » on P07); P09
  banner, breadcrumb without house selector, members named next to the avatars (no « Partagée · rôle » line); P11
  single 720 px column without side menu (profile « Enregistrer » kept); P13 illustration; M1 colour picker (6
  swatches, radio group); M5 wording « Invitation en attente » / « Renvoyer » (no email is sent).
- **Kept on purpose** (arbitrations, `docs/design/01-refonte-ux.md` § 6): frozen legal texts (only restyled), C7 footer
  on every page, separate Brand / Model fields in M2, real password rule in the P03 hint.
- **E2E** updated for the new structures (house cards, breadcrumb, P11 without side menu, public header, app icon);
  `gdpr-*.spec.ts` selectors untouched.

### Backend — house colour and banner data (`specs/ux/README.md` écart 1)

No more house photo: each house has a banner colour (C4 cards, P09 banner, P04 invitation, P05 tile).
- **Palette** `Core/HouseColors.cs`, order = README « Couleurs de maison » = rotation order:
  `indigo` #6366f1, `orange` #ea580c, `green` #16a34a, `sky` #0284c7, `yellow` #ca8a04, `pink` #db2777.
  OpenAPI enum `HouseColorKey` (same values); `Application/Common/HouseColorKeys.ToKey` maps the generated
  request enum to the stored key (a unit test guards spec ↔ palette drift).
- **Rotation rule** (`HouseColors.Next`): per owner (`House.UserId`), the least-used key among the houses they own,
  ties broken by palette order → 1st house indigo, 2nd orange, … 7th indigo again; a colour freed by a deletion is
  reused first. Houses shared with the caller do not count.
- **API**: `colorKey` on `House` / `HouseSummary` / `HouseDetail` (so also on POST/PUT responses);
  `HousesListResponse.nextColorKey` (colour the caller's next house gets — P05 tile, computed from the list, no extra
  query); `InvitationInfo.houseColorKey` (P04 banner). `CreateHouseRequest.colorKey` optional (omitted/null =
  rotation), `UpdateHouseRequest.colorKey` optional (owner only, omitted/null keeps it). Unknown value (string or
  out-of-range integer — the global `JsonStringEnumConverter` accepts integers) → 400 (`ContractValidation.cs`).
- **Migration** `20260929185025_AddHouseColorKey`: `Houses.ColorKey varchar(16) NOT NULL DEFAULT 'indigo'`, then
  `AddHouseColorKey.BackfillSql` assigns the rotation to existing rows per owner ordered by `CreatedAt`, `Id`
  (tested in `HouseColorTests`). Down drops the column.
- **Global status** (header badge C1/C2, favicon): no new field — derived from `GET /dashboard` counters
  (`overdueCount > 0` → overdue, else `pendingCount > 0` → pending, else up to date).
- **RGPD**: not personal data (a palette key) — classified non-personal in `PseudonymizationTests`; no register change.
- Web client: `Api/Dtos.cs` (`HouseColorKeys` constants, `ColorKey` on HouseDto/HouseSummary/HouseDetail/
  CreateHouseRequest, `NextColorKey`, `InvitationInfo.HouseColorKey`).
- **Banner chips + « Partagée »** (C4 cards P07/P08, P04 banner):
  - `HouseSummary.deviceTypes` (so also `HouseDetail`): `Device.Type` of every device, one per device (duplicates
    kept), oldest device first (`CreatedAt`, then `Id`) — complete list, length = `devicesCount`; the UI truncates
    (« +n »). Order computed in memory by `Application/Common/DeviceChips.TypesInCreationOrder` so list, detail and
    invitation agree (SQL uuid order ≠ .NET `Guid` order). The list adds one constant second SQL statement
    (all devices of the accessible houses), still no N+1.
  - `HouseSummary.membersCount`: owner + accepted members (`HouseMembers` rows other than the owner; pending
    invitations excluded) — available for « n membres ». The « Partagée » badge of a card does **not** use it: it
    means shared **with me** (`userRole ≠ Owner`), so an owned house with members shows no badge (as in the mockup).
  - `InvitationInfo.houseDeviceTypes` (public endpoint): device types only (never names/brands/models/maintenance),
    and only while the invitation is usable — empty list once answered/cancelled/expired (same minimisation as
    `email`). Not personal data; no register change.
  - Tests: `HouseBannerDataTests` (integration), `DeviceChipsTests` (unit).

## Recent Changes (2026-09-28) — RW invites tenants again; legal texts updated (policy 2026-09-28)

Product-owner / privacy-referent decision of 2026-09-28.

- **R5 amended** (`Common/HousePermissions.cs`): a **RW collaborator can invite a tenant, and only a tenant**; everything
  else about members (inviting RW/RO, role change, tenant rights, removal) stays **owner-only**.
  `HousePermissions.Inviters = { Owner, CollaboratorRW }` + `CanHandleInvitation(callerRole, invitedRole)` (owner: any;
  RW: `Tenant` only). `HouseMemberService`: `CreateInvitationAsync` (RW + non-tenant role → 403 `forbidden`),
  `GetHouseInvitationsAsync` (owner sees all, RW only tenant invitations; RO/tenant 403), `ResendInvitationAsync` /
  `RevokeInvitationAsync` (RW allowed on tenant invitations only, whoever created them). `IHouseMemberService.EnsureAccessAsync`
  now returns the caller's `HouseRole`.
- **Capabilities**: new `canInviteTenants` (owner + RW) next to `canManageMembers` (owner) — `specs/openapi.yaml`
  (`Capabilities`, invitation endpoints' summaries), `CapabilitiesDto`, `src/HouseFlow.Web/Api/Dtos.cs`.
- **Frontend**: P09 shows the ⋯ menu when `canManageHouse || canInviteTenants` (RW sees only « Membres ») and makes the
  avatars clickable with `canInviteTenants`. `MembersModal` parameter `CanManageMembers`: false = restricted mode
  (roles as text `member-role-label`, no ✕, invite role fixed to Locataire `invite-role-fixed`).
  `AcceptInvitation` shows the Art. 14 notice (`invitation-privacy-notice`) to signed-in invitees too.
- **Legal texts (FR + EN)** rewritten by the privacy referent: CGU § 4 (tenant creates **and edits** records, never
  deletes; RW invites tenants only; only the owner changes roles/rights or removes), privacy § 3 table (invitee email
  collected, purpose, retention incl. « refusée » and « Renvoyer » +7 d), § 4, § 5 (RW sees tenant invitations; the
  link page shows the invitee email to whoever holds a usable link), § 14 (Art. 14 information via the invitation page;
  14(5)(b) only for other third parties), version histories; `invitations.privacyNotice` = Art. 14 notice.
  **`GdprPolicy.CurrentPolicyVersion` = `LegalConstants.PolicyVersion` = `2026-09-28`** (every user re-accepts via the
  banner); `gdpr-consent-legal.spec.ts` default `POLICY_VERSION` follows.
- **GDPR docs**: register v1.3 (TR-03 inviters/recipients/measures, TR-02 note, points 11 and 12 closed), LIA v1.1
  (§ 3 balancing test redone for the invitee email), `docs/design/01-refonte-ux.md` R5 table.
- **Tests**: integration `RbacPermissionTests` (RW invites tenant / 403 `forbidden` for RW-RO roles / list filtered),
  `InvitationTests.ResendAndCancelInvitation_RW_OnlyTenantInvitations`, capabilities tuples; unit
  `HousePermissionsTests`; E2E `rbac-ui.spec.ts` (RW ⋯ menu limited to « Membres », RW invites + re-sends + cancels a
  tenant invitation in restricted M5), `onboarding.spec.ts` (new notice text).

## Recent Changes (2026-09-27) — Refonte UX (frontend + API, docs/design/01-refonte-ux.md)

Full UX redesign (spec `docs/design/01-refonte-ux.md` + spec v2 HTML, now `specs/ux/spec-refonte-v2.html`; pages P01–P15, modals M1–M7,
components C1–C8, rules R1–R7). The durable description of the new frontend is in *Frontend Architecture*, *Design
System*, *Internationalization*, *Loading UX* and *Testing* above; this entry lists what changed.

### Frontend
- **Pages**: every screen rewritten on the wireframe tokens. New P01 landing (`DemoData` preview), P05 `/setup/house` and
  P06 `/setup/devices` (onboarding with stepper), P13 404/403 page (`ErrorLayout`, header when signed in); P11 split into
  sections (`#profil #preferences #donnees #api #suppression`, theme + language moved there from the header; the email
  reminders section is out of scope). `/houses/new` and `/houses/{id}/devices/new` removed (modals M1/M2).
- **Shell**: `AppShell` (C1 header ≥ 640 px, C2 tab bar < 640 px with the « à traiter » `NavBadge`, C8 banner, C7 footer,
  C5 toast), new `SetupLayout` / `ErrorLayout` / `ProtectedLayoutBase` (R6 `returnUrl` end to end, history replaced),
  locale-less routes redirected to `/fr/…`, signed-in `/login?returnUrl=X` → X.
- **Components**: `AsyncSection` (C6), `MaintenanceRow` (C3 « C'est fait » + undo toast), `MaintenanceActions`, M3/M4
  modals, `LastMaintenancePicker`, `DeviceRow` (C4), `Modal` form mode + `ConfirmDialog` (M6),
  `MenuButton`/`OverflowMenu`, `ProgressRing`, `StatusBadge`, `Avatar(Stack)`, `Stepper`, skeletons (the house rows,
  house selector and status dot of that version were replaced on 2026-09-29 by `HouseCard` / `Breadcrumb` / pills).
  Removed: `NewHouse`, `NewDevice`, `AddMaintenanceTypeDialog`, `LogMaintenanceDialog`, `MembersSection`, `StatCard`,
  `ScoreRing`, `Breadcrumb`.
- **Services**: `ToastService`, `NavCounterService` (counters from `GET /dashboard`), `SessionService` (single logout path,
  restricted refresh → `/login?reason=restricted`).
- **Rules/**: `ParisClock`, `StatusRules` + `DateFormatter` (R4: overdue always relative), `PeriodicityRules`,
  `InitialDueRules` — unit tests in `tests/HouseFlow.UnitTests/Web/` (linked sources).
- **Permissions in the UI** come only from `capabilities` (no role checks); cost/provider hidden when `canViewCosts` is false.
- **Robustness**: network errors in modals/actions → error banner/toast; culture-aware cost parsing (fr comma, en dot);
  date inputs carry `lang`; 44 px touch targets on mobile; no translucent hovers.
- **Tailwind v4 port** of all redesign tokens (see the Tailwind entry below).
- **E2E**: selectors moved to `data-testid`/roles; `authenticatedPage` goes through P03 → P05; new `setup-page.ts`,
  `maintenance-seed.ts`, `db.ts`, `landing`/`dashboard`/`restricted-account` specs; theme tests moved to `/settings`.
  `gdpr-*.spec.ts` untouched except the post-registration URL (`/fr/setup/house`) in `gdpr-consent-legal.spec.ts`.

### Backend / API

Contract first: `specs/openapi.yaml` now also documents members, invitations, `maintenance-instances`, `/dashboard`,
`/users/settings`, `/collaborators` and `/auth/revoke` (403/404 responses documented, `date` vs `date-time` normalised).

- **R1 status, single source**: `IMaintenanceCalculatorService` (`Today`, `CalculateNextDueDate`, `CalculateStatus`,
  `Summarize`) is the only place computing statuses. "Today" is the **Europe/Paris** calendar day (`Common/ParisClock.cs`;
  containers run in UTC). Status values `overdue` / `pending` (due ≤ 30 days) / `up_to_date`, plus `none` for a
  device/house without any maintenance type. The calculator takes an optional `TimeProvider` (tests pin the clock).
  `NotInFutureAttribute` / record-date checks compare the date part to Paris "today" (a "C'est fait" between 00:00 and
  02:00 Paris was refused before).
- **R2 due dates, never null**: from the last record + periodicity; without history, `MaintenanceType.BaselineDueDate`
  (creation date for « Plus ancien », creation + 30 days for « Je ne sais pas »; null on legacy rows ⇒ creation + 30 d).
  Creating a maintenance type — or a device with its catalogue type (`CreateDeviceRequest.maintenanceType`, one
  `SaveChanges`) — takes `lastMaintenance { kind: Unknown | Older | Month, year, month }`; `Month` creates a record on
  the 1st of the month with the note « Date approximative (mois) » (`Services/MaintenanceTypeFactory.cs`). Deleting a
  record needs no recomputation (derived on read).
- **Periodicity**: enum gains `Biennial` (stored as int 5 — appended); `Custom` now prefers `CustomMonths` (1–120,
  « Tous les n mois / ans ») over the legacy `CustomDays`. Leaving `Custom` clears both.
- **Dashboard** `GET /api/v1/dashboard`: every task to handle (overdue + 30 days) over all visible houses, no limit,
  sorted by due date; counters `toHandleCount` (nav badge), `overdueCount`, `pendingCount`, `upToDateCount`,
  `totalCount`; `nextTask` (soonest up-to-date task). Each task carries `canLogMaintenance`.
- **Summaries**: house and device summaries/details expose `status`, `upToDateCount`, `maintenanceTypesCount`,
  `overdueCount`; device `pendingCount` no longer includes overdue. `score` / `globalScore` kept but obsolete (R3).
- **R5 permissions** (`Common/HousePermissions.cs`, single source): tenants may log **and edit** records (not delete);
  record deletion = owner/RW; invitations and members = **owner only** (RW could invite tenants before — *amended
  2026-09-28: RW can invite a tenant again, see that entry*). House and
  device details expose `userRole` + `capabilities { canLogMaintenance, canEditDevices, canDelete, canManageHouse,
  canManageMembers, canViewCosts }` (+ `canInviteTenants` since 2026-09-28). Device detail now sends `houseName`.
- **403/404 convention**: 404 = unknown id, 403 = exists but not accessible — `GET /houses/{id}` of a non-member is now
  403 (was 404), like devices.
- **Errors**: every domain error is RFC 9457 ProblemDetails with a machine `code` (`Common/ErrorCodes.cs`,
  `API/Filters/DomainExceptionFilter.cs` + `ApiProblem`): `invalid_credentials`, `account_restricted` (login **and**
  refresh), `invalid_refresh_token`, `email_taken` (register / profile 409), `export_rate_limited` (429),
  `invitation_invalid`, `invitation_email_mismatch`, `invitation_already_pending`, `invitation_limit_reached` (400,
  create / resend beyond 20 pending invitations per house), `already_member`, `own_invitation`,
  `forbidden`, `not_found`. 403 bodies are no longer empty.
- **Registration no longer creates « Ma maison »** nor an Owner membership; the first house comes from onboarding
  (P05, `POST /houses`). With `?invitationToken=`, the invitation is validated first (unknown/expired → 400
  `invitation_invalid`; different email → 400 `invitation_email_mismatch`; nothing is created) then accepted in the
  same save; `AuthResponse.joinedHouseId` tells the frontend where to go. The demo seed still creates « Ma maison ».
- **Invitations**: `email` required at creation (stored in `Invitations.Email`, no email is sent — the owner copies the
  link), new `Declined` status + `POST /invitations/{token}/decline`, `POST /invitations/{id}/resend` (new token,
  expiry +7 d; old link stops working), cancel = `DELETE /invitations/{id}` (owner only). The owner's list shows
  pending **and expired** invitations (`isExpired`) so they can be re-sent. Public `GET /invitations/{token}` adds
  `houseId`, `email`, `status`, `isAlreadyMember` (with a JWT; null anonymous) and finally fills `invitedByName`
  (the inviter was never loaded). Members are listed owner first.
- **Invitations, hardening round**: accept **and** decline are reserved to the invitee (account email == invitation email,
  trimmed, case-insensitive; legacy rows without email unchecked) → `400 invitation_email_mismatch`; accept checks in the
  order invalid/expired → `own_invitation` → `already_member` → email. Public `GET /invitations/{token}` returns `email`
  only while the invitation is usable (pending, not expired). Resend respects the 20-pending limit and answers
  `409 invitation_already_pending` when another pending invitation targets the same email; cancelling a non-pending
  invitation → `400 invitation_invalid`.
- **404 everywhere for an unknown house id** (devices, members, invitations lists/creation, maintenance endpoints); bare
  `NotFound()` results carry `code: not_found`. `PUT /members/{id}/role` rejects undefined enum values (400).
  `GET /users/me/export?format=xx` and `/auth/revoke` 400 are ProblemDetails.
- **PUT = replacement** for M2 / M3 edits: `UpdateDeviceRequest` `brand`/`model`/`installDate` and
  `UpdateMaintenanceInstanceRequest` `cost`/`provider`/`notes` take the value sent — omitted or null **clears** them
  (required fields `name`/`type`/`date` omitted = kept; a caller without `canViewCosts` cannot touch cost/provider).
  `UpdateHouseRequest` stays a partial update.
- **More DTO fields**: device summary `nextDueDate` / `nextMaintenanceName`; dashboard tasks carry `capabilities`
  (`canViewCosts` for M3 from P07). The frontend no longer calls `GET /upcoming-tasks` (endpoint kept; `ApiService.GetUpcomingTasksAsync` removed).
- **Migration** `20260927063741_RefonteUxMaintenanceAndInvitations`: `MaintenanceTypes.CustomMonths`,
  `MaintenanceTypes.BaselineDueDate`, `Invitations.Email` (255), `Invitations.DeclinedAt`. `Down` first maps rows the old
  code cannot read: Biennial → Custom 730 days, Custom n months → `CustomDays = n × 30`, `Declined` → `Revoked`.
- **RGPD**: invitee email = new personal data of a (often unregistered) third party → register TR-03 v1.2, retention
  policy v1.1 (purged with the invitation, expiry + 30 d), excluded from the audit trail, pseudonymized by `dbtools`
  (+ `verify.sql` check). Open point n° 11 of the register (legal texts, LIA § 3, Art. 14 information) — closed
  2026-09-28, see that entry. Export CSV gains `customMonths`.
- **Visual QA pass** (FR/EN × light/dark × 1280/390, every screen and state): `<html lang>` follows the `/{locale}`
  prefix (`index.html` at boot + `LocaleBoundary` → `hf.setLang` on navigation); P02/P03 placeholders are i18n keys
  (`auth.*Placeholder`; FR values kept identical because frozen GDPR E2E specs select on them); unused
  the old `Components/Breadcrumb.razor` removed (reintroduced on 2026-09-29 for `specs/ux`).
- **Frontend client** (`src/HouseFlow.Web/Api/Dtos.cs`, `ApiService.cs`, hand-written): mirrors all of the above;
  `ApiException` now carries `StatusCode` + `Code` (ProblemDetails `code`) — see `ApiErrorCodes`.
- **Tests**: integration tests create their house explicitly (`TestHelpers.CreateHouseAsync`); new coverage for the
  dashboard, R1/R2, « Dernier entretien », periodicities, R5 capabilities, invitation email/decline/resend,
  registration through an invitation, ProblemDetails codes.

## Recent Changes (2026-09-27) — Tailwind CSS v3 → v4

Reprise de la PR Dependabot qui passait `tailwindcss` en 4.3.3 sans migration (build Web et image
Docker rouges : `tailwindcss: not found`, la CLI vit désormais dans `@tailwindcss/cli`).
- Migration faite avec l'outil officiel `@tailwindcss/upgrade` puis ajustée : config CSS-first dans
  `Styles/app.input.css` (`@theme inline` pour les couleurs/rayons shadcn, `@plugin 'tailwindcss-animate'`,
  `@custom-variant dark (&:is(.dark *))`, `.legal-content` devenu un `@utility`). `tailwind.config.js`,
  `postcss.config.js`, `postcss` et `autoprefixer` supprimés (v4 gère les préfixes via Lightning CSS).
- Sources scannées explicites (`source(none)` + `@source` razor / razor.cs / `Api/**/*.cs` / index.html),
  équivalent strict de l'ancien `content` ; `TailwindInput` du csproj aligné.
- Classes renommées dans 18 `.razor` : `shadow-sm`→`shadow-xs`, `backdrop-blur-sm`→`backdrop-blur-xs`,
  `bg-gradient-to-*`→`bg-linear-to-*`, `flex-shrink-0`→`shrink-0`, `min-w-[10rem]`→`min-w-40`.
- Compatibilité v3 conservée dans `@layer base` : couleur de bordure par défaut (`border-border`, étendue aux
  pseudo-éléments), placeholders gray-400, `cursor: pointer` sur les boutons.
- Écarts résiduels acceptés : pile `font-sans` par défaut de Tailwind 4.3 (identique sur macOS/Windows),
  Preflight v4 qui remet à 0 le padding natif de 1px des `<th>/<td>`.
- Refonte UX portée sur v4 : les tokens (`primary`/`destructive`/`warning` avec `strong`/`soft`/`hover`/`border`,
  statuts `overdue`/`due`/`ok`, `toast`) sont dans le même `@theme inline` (variables HSL light/`.dark` dans
  `@layer base`), les briques `hf-*` restent du CSS `@layer components` (sélecteurs composés, jamais sous variante).
  `@source` couvre aussi `Components/`, `Features/`, `Localization/**/*.cs` (classes composées en C#).
  Hauteurs de ligne `text-xs`…`text-4xl` remises en valeurs absolues v3 (`@theme`) : en v4 ce sont des ratios qui
  suivent `text-[13px]` & co. Piège v4 : l'ordre des utilitaires `display` a changé (`hidden` sort avant
  `inline-flex`/`inline-block`) — ne pas empiler `hidden sm:inline-flex` sur un composant qui pose déjà
  `inline-flex`, utiliser `max-sm:hidden`.

## Recent Changes (2026-09-26) — `LastLoginAt` mesure l'activité, pas la saisie du mot de passe

Un compte **actif** pouvait être supprimé par la purge des comptes inactifs. `Users.LastLoginAt`
n'était écrit que par `AuthService.LoginAsync`, jamais sur le chemin de rafraîchissement — or une
session « Se souvenir de moi » est glissante sur 365 jours, renouvelée à chaque rafraîchissement.
Un utilisateur qui ouvre l'application tous les jours sans jamais ressaisir son mot de passe avait
donc un `LastLoginAt` figé, et la procédure de purge à 3 ans (`data-retention-policy.md` § 5) ne
regardait que cette colonne. Suppression d'un compte actif, avec ses maisons et son historique :
violation de l'Art. 5(1)(d) du RGPD, exactitude.

- **`AuthService.RefreshTokenAsync`** appelle désormais `TouchLastLoginAsync`. Deux précautions
  rendent l'écriture négligeable, et elles sont le cœur du correctif :
  - **Seuil de 24 h** (`AuthService.LastLoginPrecision`). Un jeton d'accès vit 15 minutes : écrire
    à chaque rafraîchissement coûterait un `UPDATE` par quart d'heure et par utilisateur, pour
    servir une règle à 3 ans qui se moque de la minute. Le test est une comparaison de dates sur
    l'utilisateur **déjà chargé** par `.Include(rt => rt.User)` — aucun aller-retour en base. Sur
    le rafraîchissement courant, zéro écriture supplémentaire ; au plus une par utilisateur et par
    jour.
  - **Hors change tracker** : `ExecuteUpdateAsync`, comme le fait déjà `LoginAsync`. L'instance
    chargée n'est volontairement pas alignée — la marquer « modifiée » ferait réécrire la colonne
    au prochain `SaveChanges` de la requête.
- **`HouseFlowDbContext`** exclut `LastLoginAt` du journal d'audit, aux côtés de `CreatedAt` et
  `ModifiedAt`. Sans cela, l'intercepteur aurait produit une entrée d'audit par écriture — un
  événement quotidien par utilisateur, dans une table qui a un an de rétention et un job
  d'anonymisation. L'exclusion est posée dans l'intercepteur plutôt que laissée à la discipline des
  appelants : aucune écriture, même par le change tracker, ne peut plus polluer le journal.
- **Trois tests unitaires** (horodatage périmé → rafraîchi ; récent → intact ; rien dans l'audit) et
  **un test d'intégration** sur PostgreSQL réel, le provider InMemory ne sachant pas exécuter
  `ExecuteUpdate` — c'est-à-dire précisément le chemin de production.
- **`data-retention-policy.md` § 5.1** : le second critère de la requête d'identification (absence
  de jeton de session récent), ajouté en garde-fou avant ce correctif, est retiré — il est devenu
  superflu. Un **contrôle de cohérence** le remplace : une requête qui doit renvoyer zéro ligne, et
  dont la moindre ligne signifie que la mise à jour au rafraîchissement a régressé et qu'aucune
  suppression ne doit être exécutée.

## Recent Changes (2026-09-26) — Modes opératoires des contrôles annuels RGPD

Le dossier de conformité annonçait deux contrôles annuels (Art. 32(1)(d)) sans dire comment les
conduire : un test de restauration réduit à une ligne de tableau, et un exercice de violation
décrit par son scénario mais sans déroulé. Les deux sont maintenant exécutables par quelqu'un qui
ne les a jamais faits.

- **`docs/security/backup-restore-drill.md`** (nouveau) — mode opératoire du test de restauration :
  relevé de `earliestRestoreDate`, restauration PITR sur serveur temporaire, cinq requêtes de
  vérification (schéma, migrations, volumétrie, fraîcheur, clés étrangères non validées),
  destruction vérifiée, fiche de preuve et historique des tests. Deux contraintes réelles y sont
  documentées, découvertes en lisant l'infrastructure : le subnet `snet-db` est un **/28** — le
  minimum de Flexible Server — donc la copie exige un subnet délégué créé pour l'occasion
  (`snet-db-restore`, `10.0.1.0/28`) ; et l'authentification étant **Entra uniquement**
  (`password_auth_enabled = false`), la connexion passe par un jeton
  `https://ossrdbms-aad.database.windows.net` valable une heure, à travers le tunnel du bastion.
- **Limite documentée plutôt que corrigée** — la politique de conservation annonçait la
  « réapplication des suppressions intervenues depuis » : le code ne le permet pas. L'entrée
  d'audit `AccountDeleted` (`UserAccountService`) est écrite **sans identifiant**, par construction,
  pour qu'une suppression ne laisse pas de trace rattachable. Les suppressions ne s'obtiennent donc
  que par différence avec la base vivante, et si celle-ci est perdue, la résurrection des comptes
  supprimés est une **violation de données** au sens de l'Art. 4(12), pas un incident
  d'exploitation. `data-retention-policy.md` § 3.2 le dit désormais.
- **`breach-notification-procedure.md` § 13.0** (nouveau) — préparation à J-7 (dont l'accès au
  guichet de l'APD, le point qui échoue le jour J), règle d'or « aucune action réelle en
  production », déroulé minuté d'une demi-journée en sept séquences avec un livrable écrit par
  séquence, et trois règles pour conduire l'exercice en opérateur unique (écrire sa réponse avant
  de vérifier, chronométrer pour de vrai, interdire le « je saurais faire »).
- **`subprocessors.md` § 2.5.1** (nouveau) — l'instruction précédente envoyait chercher dans le
  portail Azure une « version du DPA acceptée » qui **n'y figure pas** : le DPA est incorporé par
  référence, il ne s'accepte ni ne se signe séparément, et l'entrée *Agreements* n'existe que pour
  les contrats MCA et EA. Le § 2.5 consigne désormais un triplet démontrable (édition archivée,
  date de téléchargement, contrat de rattachement) et le § 2.5.1 donne sept étapes pour établir la
  date du contrat, de la plus directe au recours au support Microsoft.

## Recent Changes (2026-09-19) — Projections EF Core à plat au lieu de graphes d'entités (#218)

Les endpoints de lecture les plus utilisés (`GET /houses`, `GET /devices/{id}`, `GET /houses/{id}`,
`GET /houses/{id}/devices`) chargeaient un graphe d'entités complet (`Include().ThenInclude()`, avec
`QuerySplittingBehavior.SplitQuery`) puis calculaient scores/statuts en mémoire sur des entités
jamais réellement nécessaires. Sans changer une seule réponse de l'API (garde-fou : diff au bit
près, voir plus bas), ces endpoints projettent maintenant directement les colonnes dont
`IMaintenanceCalculatorService` a besoin.

- **`MaintenanceTypeSnapshot`** (`Application/Common/`) — record `(Id, Name, Periodicity,
  CustomDays, DeviceId, CreatedAt, LastMaintenanceDate)` : tout ce que le calculateur lit pour un
  type d'entretien, sans la liste complète des `MaintenanceInstance`. `IMaintenanceCalculatorService`
  gagne des surcharges `CalculateDeviceScore`/`CalculateHouseScore`/`CalculateMaintenanceTypeWithStatus`
  qui prennent des snapshots au lieu d'entités ; les surcharges historiques (entités) restent
  utilisées par `MaintenanceService` (tâches à venir, hors périmètre de cette issue).
- **`HouseService.GetUserHousesAsync`** — une seule requête SQL : maisons possédées ou dont
  l'utilisateur est membre, aplaties une ligne par (maison, type d'entretien) via `SelectMany` +
  `DefaultIfEmpty` (maisons/appareils sans type gardés), rôle résolu inline (voir plus bas). Le
  rôle et le nombre d'appareils par maison, groupés en mémoire après coup. La boucle
  `GetUserRoleAsync` par maison (N+1) a disparu.
- **`DeviceService.GetDeviceDetailAsync`** — 2 requêtes au lieu d'~5 : une pour les agrégats du
  device (`sum(Cost)`, `count(*)`, calculés par PostgreSQL, pas remontés puis sommés en C#), une
  pour les lignes à plat par type d'entretien. Les agrégats device sont délibérément **hors** de la
  projection par type : une sous-requête corrélée dans une liste `SELECT` s'exécute une fois par
  ligne, donc les y mettre aurait recalculé les mêmes agrégats une fois par type au lieu d'une fois
  par device (mesuré en cours de développement : régression au benchmark avant d'être corrigé).
  `EnsureAccessAsync` + `ShouldHideCostsAsync` (2 appels, chacun sa propre requête de rôle)
  remplacés par `IHouseMemberService.GetAccessInfoAsync` (1 requête) + dérivations pures
  `EnsureAccess`/`ShouldHideCosts` (H1).
- **`HouseService.GetHouseDetailAsync` / `DeviceService.GetHouseDevicesAsync`** — même traitement
  (lignes à plat device × type d'entretien), en 2 requêtes plutôt qu'une seule : contrairement à
  `GetUserHousesAsync`, il faut ici les colonnes propres du *device* (pas seulement des agrégats),
  ce qui exige un flatten à deux niveaux (maison→appareil→type) ; EF Core ne traduit pas de
  sous-requête corrélée à travers un membre (même scalaire) d'une forme déjà `.Select()`-projetée,
  seulement à travers une entité brute. Toujours une nette baisse par rapport aux 3 requêtes
  split-query + le graphe complet d'avant.
- **`IHouseMemberService.ProjectHousesWithRole(IQueryable<House>, Guid)`** — résout le rôle de
  l'appelant pour un ensemble de maisons en une sous-requête corrélée par maison (la règle
  ownership-d'abord-puis-membership vit à un seul endroit). *Non utilisée* par
  `GetUserHousesAsync` : EF Core ne sait pas traduire une sous-requête supplémentaire corrélée à
  travers un membre d'une forme déjà projetée par un `.Select()` externe (testé avec un record, un
  `ValueTuple`, et un `join` — même échec de traduction dans les trois cas) ; le rôle y est donc
  résolu par une expression identique mais écrite directement sur l'entité `House` de la requête,
  avec un commentaire renvoyant à cette limitation. La méthode reste disponible et correcte pour
  un appelant qui n'a pas besoin d'aplatir davantage.
- **Piège du diff bit-à-bit** — deux différences invisibles aux tests unitaires, trouvées par le
  diff des réponses JSON avant/après (pas par les tests) : (1) `Sum(x => (decimal?)i.Cost)` traduit
  par Npgsql en `COALESCE(sum(...), 0.0)` ; quand aucune instance n'a de coût, ce `0.0` (scale 1)
  sérialise différemment du `0` (scale 0) que produit un `Sum()` en mémoire sur une séquence vide.
  Corrigé en calculant côté C# si une instance a un coût non nul (`HasAnyCost`) et en forçant un
  littéral `0m` sinon. (2) L'ordre des lignes (maisons, appareils, types) doit rester un `ORDER BY`
  SQL sur la même colonne qu'avant (id), traduit avant `ToListAsync()` — un tri en mémoire après
  coup ne reproduit pas forcément l'ordre octet-à-octet de PostgreSQL sur un `uuid`.
- **Infrastructure** — `No Reset On Close=true` ajouté aux chaînes de connexion locale
  (`HouseFlow.AppHost/Program.cs`) et de déploiement (prod/preprod/ephemeral). `Hangfire:Enabled`
  (config, défaut `false`) : Hangfire ne tourne plus qu'en Production (`Terraform`), où
  `CleanupExpiredInvitationsJob` est réellement utile ; ailleurs, plus de polling PostgreSQL de
  fond pour un job qui ne sert à rien en dev/CI/preprod/PR éphémères.
- **Validation** — `dotnet test` vert (57 unitaires + 164 intégration, inchangés dans leur
  comportement attendu). Diff bit-à-bit des réponses JSON (binaire `main` vs binaire optimisé, même
  base peuplée via l'API publique) sur `GET /houses`, `GET /houses/{id}`, `GET /houses/{id}/devices`,
  `GET /devices/{id}` (5 devices couvrant maison sans adresse, device sans type, type sans
  instance, instances avec coût nul mélangé à des coûts réels, et instances toutes à coût nul) :
  identiques à l'octet près après les deux corrections ci-dessus.

**Mesures avant/après** (`npx autocannon`, VM partagée à 1 processus API + PostgreSQL locaux —
pas la machine dédiée à 4 cœurs de la mesure de référence de l'issue, donc des gains plus modestes
mais dans le même sens) :

| Endpoint, jeu de données, concurrence | Avant | Après |
|---|---|---|
| `GET /houses` (3 maisons, 30 appareils, 90 types, 180 instances), c=1 | 131 req/s | 328 req/s |
| `GET /houses`, c=64 | 616 req/s | 1664 req/s |
| `GET /devices/{id}` (1 device, 20 types, 400 instances), c=1 | 178 req/s | 204 req/s |
| `GET /devices/{id}`, c=64 | 869 req/s | 1064 req/s |
| `GET /devices/{id}` (1 device, 3 types, 6 instances — proche du réel), c=1/c=64 | ~stable | ~stable |

Le gain sur `GET /devices/{id}` croît avec le volume d'instances (matérialisation évitée), et reste
proche de zéro pour un device de taille réaliste sur une VM locale sans latence réseau — cohérent
avec la note de l'issue selon laquelle la matérialisation d'entités, pas les allers-retours réseau,
domine le coût.

## Recent Changes (2026-09-23) — File d'attente réelle sur le verrou `ovh-dns-zone` (#252)

- **Problème résiduel après #238** : le verrou est court (quelques secondes) mais un groupe de
  concurrence GitHub garde par défaut (`queue: single`) au plus un job en attente — un nouveau
  venu annule celui qui patiente. Trois pushes à quelques secondes d'intervalle suffisaient encore
  à annuler un job DNS
- **Correctif** : `queue: max` sur les trois jobs du groupe `ovh-dns-zone`
  (`deploy-preview-dns` et `cleanup-preview` dans `pr-preview.yml`, `apply-prod-dns` dans
  `pipeline.yml`) — jusqu'à 100 jobs en attente, exécutés dans l'ordre, sans annulation.
  Incompatible avec `cancel-in-progress: true`, jamais utilisé sur ces jobs
  (documenté dans `specs/infrastructure.md` et les commentaires des deux workflows)
- **Inchangé** : le groupe par PR `pr-preview-<n>` (niveau workflow) reste en `queue: single` — un
  nouveau push de la même PR doit remplacer celui qui attend, pas s'y mettre en file. Les groupes
  `approve` (annulation) et `reaper` (hors du groupe `ovh-dns-zone`) ne changent pas non plus

## Recent Changes (2026-09-24) — Spike `claude --cloud` depuis GitHub Actions : pas faisable (#253)

- **Question** : une issue labellisée `claude` peut-elle correspondre à une seule conversation
  Claude Code web, les commentaires suivants étant envoyés à la session existante via
  `claude -p "<commentaire>" --cloud <session_id>` depuis `claude-issue.yml`, authentifié par
  `CLAUDE_CODE_OAUTH_TOKEN` (`claude setup-token`) ?
- **Réponse : non, avec le CLI 2.1.28x.** Créer une session (`claude --cloud "desc"`) exige un TTY
  interactif (« `--cloud requires an interactive terminal` », indépendant de l'auth) et, sur un CLI
  fraîchement installé, passe par l'onboarding interactif. Messager une session existante marche en
  non-TTY (`< /dev/null | cat`), mais **seulement après un `claude login` complet** : avec le
  setup-token seul → « Unable to get organization UUID » ; avec le setup-token + `oauthAccount`
  dans `~/.claude.json` → « Session expired. Please run /login » (le CLI exige alors les vrais
  credentials de login, à refresh token). Aucune combinaison headless ne passe sans copier des
  credentials interactifs, exclu par principe. Tests menés dans le conteneur cloud et sur un poste
  WSL ; le workflow de sonde prévu n'a pas été ajouté (session automatisée non autorisée à créer un
  workflow CI). Détail des commandes et sorties exactes : commentaires de #253
- **Identifiants** : le CLI reconnaît `session_…` (identifiant interne), pas l'URL publique
  `claude.ai/code/cse_…` que poste `claude-issue.yml`, prise pour une description
- **Direction retenue (#258)** : le dialogue d'une session automatisée passe par la **PR**, où
  `subscribe_pr_activity` livre déjà commentaires, reviews et CI dans la même conversation.
  Le mécanisme actuel (routine API, une session neuve par déclenchement) reste en place pour
  l'issue. Une session automatisée se renomme dès l'issue lue (`set_session_title`), au format
  fixe `[<n>] Issue - <titre de l'issue>` (CLAUDE.md, Phase 2), pour rester identifiable dans
  la liste des sessions

## Recent Changes (2026-09-23) — Le verrou OVH ne couvre plus que les écritures DNS (#238)

- **Cause des previews « cancelled »** : un groupe de concurrence GitHub ne garde qu'UN job en
  attente — un troisième arrivant annule celui qui attendait (`cancel-in-progress: false` ne
  protège que le job en cours). Le verrou `ovh-dns-zone` couvrait tout `deploy-preview` (~25 min
  à la création) : les previews poussées pendant ce temps finissaient annulées sans avoir eu de
  runner (#233, #235, #237, #163 le 2026-09-23)
- **Trois racines par instance, appliquées dans l'ordre** : `environment` (Azure, calcule les
  enregistrements et expose `dns_records`, `api_custom_domain`, `frontend_custom_domain`) →
  `dns` (écrit les enregistrements OVH, sous le verrou, quelques secondes) → `custom-domains`
  (attente de propagation 60 s, liaisons Container App et Static Web App). `dns` et
  `custom-domains` lisent le state d'`environment` (`terraform_remote_state`) et ne prennent que
  `name` et le storage account des states. States : `<racine>-<nom>.tfstate`
- **Workflows** : `pr-preview.yml` → `deploy-preview-infra` (apply `environment`, restauration,
  frontend) → `deploy-preview-dns` (verrou) → `deploy-preview` (liaisons, fumée, commentaire).
  `pipeline.yml` → `apply-prod` (plan approuvé, frontend) → `apply-prod-dns` (verrou, plan +
  `tf-plan-guard.sh dns`, qui refuse toute suppression d'enregistrement hors `[dns-allow-destroy]`
  ou entrée `allow_dns_destroy`) → `apply-prod-custom-domains` (liaisons, fumée). Le cleanup et le
  reaper suppriment les trois blobs de state
- **Transition sans migration de state** : le premier apply d'`environment` sur un state d'avant
  #238 détruit ses enregistrements OVH, son `time_sleep` et ses deux liaisons ; `dns` et
  `custom-domains` les recréent juste après. En prod, `www` et `api` sont coupés quelques minutes
  (le temps que la Static Web App réémette son certificat) — accepté. `environment` garde les
  providers `ovh` et `time` pour pouvoir détruire ces ressources : à retirer une fois la prod
  déployée
- **Plus aucun `-target`** : chaque job applique une racine entière, l'ordre des dépendances vit
  dans le découpage des racines et l'enchaînement des jobs, pas dans une liste de ressources

## Recent Changes (2026-09-23) — Retry EF Core des transactions Serializable (#198)

- **Cause** : la branche production de `Program.cs` enregistrait le `DbContext` sans stratégie de retry, alors qu'Aspire (`AddNpgsqlDbContext`, local/CI) active `EnableRetryOnFailure()` par défaut. Un `40001` (échec de sérialisation Postgres) dans `AcceptInvitationAsync` remontait donc en 500 en production.
- **Correctif** : `npgsqlOptions.EnableRetryOnFailure()` côté production, mêmes valeurs que les défauts Aspire (6 tentatives, délai max 30 s). Npgsql classe `40001` et `40P01` comme transitoires, pas besoin d'`errorCodesToAdd`.
- **Pattern** pour toute transaction explicite : `CreateExecutionStrategy().ExecuteAsync(...)`, `ChangeTracker.Clear()` en tête du délégué (une tentative annulée laisse des entités modifiées suivies), `await using` de la transaction sans rollback manuel. Aucun effet de bord hors base dans le délégué (il est rejoué).
- **HTTP** : `RetryLimitExceededException` (tentatives épuisées, conflits répétés comme panne de base) est mappée en 503 par `DomainExceptionFilter`, pour toute l'API — un 5xx reste visible du monitoring.
- **Idempotence** : réaccepter une invitation déjà acceptée par le même utilisateur renvoie 200 (double clic, ou retry après un commit dont l'accusé de réception s'est perdu).
- `IApplicationDbContext` expose `ChangeTracker`.
- Tests : `InvitationTests` — 8 acceptations concurrentes sur des maisons différentes → toutes 200 ; deux utilisateurs sur la même invitation → un 200, un 400, un seul membre ajouté ; même utilisateur deux fois → 200 idempotent.

## Recent Changes (2026-09-23) — Aspire 13.5.4, MessagePack/OpenTelemetry.Api hors advisory (#159)

- **Constat** : `dotnet restore` remontait `NU1902`/`NU1903` pour `MessagePack` 2.5.192 (2 advisories haute
  sévérité) et `OpenTelemetry.Api` 1.14.0 (1 modérée) — dépendances **transitives** d'Aspire (13.1.0), sans
  référence directe dans le code.
- **Exposition qualifiée** : `MessagePack` n'arrive que via `Aspire.Hosting.Docker`/`.Testing` → `StreamJsonRpc`
  (communication DCP), utilisées uniquement par `HouseFlow.AppHost` (orchestration locale) et
  `HouseFlow.IntegrationTests` — ni l'un ni l'autre n'entre dans une image Docker déployée
  (`src/HouseFlow.API/Dockerfile` et `src/HouseFlow.WebHost/Dockerfile` ne copient que Core/Application/
  Infrastructure/API et Web/WebHost). `OpenTelemetry.Api` en revanche transite bien par
  `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL`, référencé par `HouseFlow.API` lui-même — donc présent dans
  l'image déployée.
- **Correctif** : bascule des quatre références Aspire (`Aspire.AppHost.Sdk`, `Aspire.Hosting.Docker`,
  `Aspire.Hosting.PostgreSQL`, `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL`, `Aspire.Hosting.Testing`) de
  13.1.0 vers **13.5.4**, qui résout `StreamJsonRpc` 2.25.29 (→ `MessagePack` 2.5.302, patché) et
  `OpenTelemetry.Extensions.Hosting` 1.15.3 (→ `OpenTelemetry.Api` 1.15.3, patché). `dotnet restore` ne
  remonte plus aucun `NU1902`/`NU1903`. `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore`
  bump de 10.0.5 à 10.0.11 en cascade (plancher imposé par la nouvelle version d'Aspire).
- **Dependabot** : `.github/dependabot.yml` ajouté (écosystème `nuget`, hebdomadaire) pour que les futurs
  advisories remontent en PR plutôt que dans le bruit du restore.

## Recent Changes (2026-09-22) — Données de prod pseudonymisées dans les previews (#199)

- **Dump nocturne** : `job-dbtools-dump` (instance permanente, cron 02:00 UTC) copie
  `houseflow_prod` (sans le schéma `hangfire`) dans une base de travail `houseflow_dumpwork`, la
  pseudonymise (`dbtools/pseudonymize.sql` : Users, RefreshTokens, ApiKeys, AuditLogs,
  Invitations, adresses des maisons, prestataires et notes d'entretien), la contrôle
  (`dbtools/verify.sql`) et publie `db-dumps/latest.dump` — rien n'est publié au moindre écart.
  Comptes préservés : `preserved_emails` de `instances/prod.tfvars` (mainteneur + démo)
- **Restauration** : `job-dbtools-restore` (instances jetables) vide le schéma `public` et
  restaure le dump en **une transaction**, une seule fois par environnement (table
  `__dbtools_restore`). `pr-preview.yml` le lance après `terraform apply` puis redémarre l'API,
  dont l'init container `--migrate` applique les migrations de la branche sur les données de prod.
  Sans dump disponible, le job avertit et l'environnement garde ses données de démo
- **Un job par instance, déduit de `expires_at`** (comme le verrou) : `dump` sur la prod,
  `restore` sur une PR (`environment/dbtools.tf`)
- **Accès au blob par identité partagée**, sur le modèle d'`id-houseflow-cert`, mais nommée par son
  droit pour ne pas confondre les deux souscriptions : `id-houseflow-dumps-writer` (`Storage Blob Data Contributor`, racine
  `shared`) côté production, `id-houseflow-dumps-reader` (`Reader`, bootstrap §5a du guide) côté jetable. La condition ABAC de `sp-prod`
  s'élargit à `Storage Blob Data Contributor` — **à refaire à la main sur l'installation
  existante** avant le premier `apply-shared` (§4a)
- **`dbtools` réécrit** : les sous-commandes mortes `roles`/`init` disparaissent, `dump`/`restore`
  parlent au Blob Storage par l'API REST avec un token d'identité managée (pas d'azure-cli). Image
  construite par `pipeline.yml` (tag CalVer) et `pr-preview.yml` (`pr-<n>`)
- **`scripts/sanitize-pii.sh` supprimé** : manuel, sans allow-list, et visant une preprod qui
  n'existe plus
- **Tests** : `PseudonymizationTests` (intégration) — base migrée par EF, seed réaliste,
  pseudonymisation + vérification, comptes préservés intacts, hash neutralisé rejeté par BCrypt,
  et classification obligatoire de toute colonne texte du modèle

## Recent Changes (2026-09-21) — Un environnement complet par instance

La refonte annoncée le 2026-09-19 est livrée, mais pas sous la forme décrite alors : le découpage
en quatre resource groups autour d'un serveur PostgreSQL partagé a été abandonné en cours de
route au profit d'environnements entièrement autonomes. Cible à jour :
`specs/infrastructure.md`.

- **Une racine Terraform unique**, `infrastructure/terraform/environment/`, instanciée par un
  `name` et un jeu de variables dans `instances/`. Les cinq racines `env-prod`, `env-preprod`,
  `env-preview`, `deploy-prod`, `deploy-preprod`, la stack `dns` et le module `modules/env` ont
  disparu. La production n'est plus un cas particulier du code : c'est l'instance dont l'échéance
  est vide et le resource group verrouillé.
- **Chaque environnement possède son serveur PostgreSQL, son VNet et son identité.** C'est le
  déplacement qui justifie tout le lot : une montée de version majeure, un changement de SKU ou
  de subnet s'éprouve désormais sur un environnement jetable, alors que la production était
  jusqu'ici le seul terrain d'essai possible. Aucun peering entre VNets, donc deux instances
  peuvent porter le même plan d'adressage.
- **`shared` réduit à ce qui ne peut appartenir à aucun environnement** : le Key Vault,
  `id-houseflow-cert`, le storage des states et le conteneur `db-dumps`. Le serveur PostgreSQL,
  le VNet et les trois identités d'environnement en sont partis.
- **Une PR reçoit un environnement complet** `pr-<n>` (~25 min à l'ouverture, ~2 min par push
  ensuite), et non plus un locataire d'un environnement `preview` partagé.
- **`preprod` a disparu, et avec lui `environment.yml` et `instances/preprod.tfvars`.** Il ne
  reste que deux instances, `prod` et `pr-<n>`. Un environnement de PR étant complet et produit
  par le même apply que la production, il valide les changements d'infrastructure dans la PR qui
  les introduit : un environnement de validation séparé ne prouvait rien de plus et se facturait
  en continu. Dans la foulée, le TTL des environnements de PR passe de douze à quatre heures
  (glissant à chaque événement de la PR), `rg_lock_enabled` et `api_min_replicas` cessent d'être
  des variables pour se déduire de `expires_at == ""` — en tant que variables, elles rendaient
  représentable l'environnement éphémère ET verrouillé, c'est-à-dire un resource group promis au
  reaper qu'il ne peut pas détruire — et `reaper.yml` tourne désormais sous l'environnement GitHub
  `preview`. L'app registration `houseflow-github-preprod` et l'environnement GitHub `preprod`
  n'ont plus aucun consommateur.
- **Frontend en Static Web App (SKU Free) partout**, production comprise. Blazor WebAssembly est
  entièrement statique : la Container App maintenait un réplica pour servir des fichiers. L'image
  `houseflow-frontend` n'est plus construite, et la SWA émet son propre certificat par délégation
  CNAME.
- **L'approbation de production arrive après le plan.** `plan-prod` publie le plan (résumé du run
  + artefact `plan-prod`), `approve-prod` le donne à lire, `apply-prod` applique **ce fichier de
  plan**. Un environnement de PR prouve que le code produit une infrastructure qui marche, jamais
  qu'appliqué à l'état existant de la prod il est inoffensif — un `replace` du serveur
  n'apparaît que dans un plan contre la prod. `apply-shared` n'est plus derrière l'approbation
  (il doit précéder le plan) mais reste couvert par `tf-plan-guard.sh`.
- **`reaper.yml`** (horaire) supprime par **tag** les resource groups expirés, jamais par state :
  un `terraform destroy` exige un state sain, or c'est précisément quand le state est perdu ou
  laissé à moitié écrit qu'un environnement devient un orphelin facturé. La production ne porte
  pas de tag `ttl` — c'est toute sa protection, et elle évite une liste d'exclusion à maintenir.
- **Deux souscriptions Azure**, production et jetables. `HouseFlow Deployer` passe au scope
  souscription avec `resourceGroups/write|delete` (un environnement jetable crée le sien, dont le
  nom n'est pas connu à l'avance). `HouseFlow Shared Tenant` a été supprimé : la racine
  `environment` ne lit plus le Key Vault par data source, son URI arrive en variable.
  **Attention : `AZURE_SUBSCRIPTION_ID` est encore un secret de dépôt** — l'isolement ne sera
  effectif que lorsque des secrets d'environnement le surchargeront (`docs/azure-setup-guide.md`
  §8b).
- **`dbtools` n'est plus appelé par aucun workflow.** L'image, les jobs et
  `scripts/ci/run-dbtools-job.sh` restent sur le disque pour #199 (restauration d'un dump
  pseudonymisé), mais chaque environnement ayant son serveur, Terraform crée la base directement
  et il n'y a plus de rôle à créer en SQL sur le serveur d'autrui.
- **Documentation remise d'équerre** : `docs/azure-setup-guide.md` (bootstrap des deux
  souscriptions, rôles, federated credentials, liste exacte des secrets et variables consommés
  par les trois workflows), `infrastructure/rbac/README.md`, `specs/architecture.md`.

## Recent Changes (2026-09-19) — Refonte infrastructure et pipeline (#222)

Décision de repartir de zéro sur l'infrastructure Azure et le pipeline CI/CD ; cible détaillée
dans `specs/infrastructure.md`. Implémentation à venir issue par issue — ce qui suit décrit ce qui
change par rapport à l'existant documenté plus bas dans ce fichier :

- **4 resource groups** au lieu d'un seul `rg-houseflow` : `shared` (PostgreSQL, Key Vault,
  tfstate, identités managées) et `preprod`/`preview`/`prod`, chacun avec son VNet et son CAE,
  peerés vers `shared` — un run preprod ne peut plus rien faire dans le RG prod.
- **3 app registrations GitHub** (une par environnement) au lieu d'une seule identité OIDC
  partagée par tous les workflows.
- **PostgreSQL reste un serveur unique partagé**, mais la frontière entre environnements passe
  par les rôles PostgreSQL, administrés par des Container Apps Jobs `dbtools`
  (`job-dbtools-roles`, `job-dbtools-init`) — le runner GitHub perd tout accès réseau direct au
  serveur.
- **Certificat wildcard** émis par le pipeline et référencé directement par chaque CAE depuis Key
  Vault (`azapi`), au lieu d'un upload manuel sur l'environnement à chaque run.
- **Un seul workflow `pipeline.yml`** remplace `infra.yml` + `deploy.yml` + `certificate.yml`,
  avec une seule approbation humaine (environnement `prod-approval`) au lieu d'une par workflow.
  `scripts/ci/wait-infrastructure.sh` disparaît (plus de workflow séparé à attendre) ;
  `scripts/ci/run-dbtools-job.sh` apparaît pour piloter les jobs `dbtools`. `scripts/sanitize-pii.sh`
  reste, mais son usage (copie prod → preprod/previews) est repris par le pipeline `dbtools`,
  spécifié dans l'issue #199.
- `infrastructure/terraform/main/` et `deploy-dns-ovh/` disparaissent au profit de `shared/`,
  `modules/env/`, `env-*/` et `dns/`.

## Recent Changes (2026-09-18) — Certificat wildcard et domaines personnalisés (#203)

Prod, preprod et previews de PR sont servis sous `houseflow.cloud` avec **un seul certificat
wildcard `*.houseflow.cloud`** (Let's Encrypt), au lieu d'un certificat géré Azure par hôte.
Cible et conventions : `specs/architecture.md` (« DNS » et « Certificat TLS »). Ce qui a été livré,
dans l'ordre :

- **Key Vault `kv-houseflow`** (`main/key-vault.tf`, mode access policies : le rôle de déploiement
  n'a pas `roleAssignments/write`) — copie durable du certificat (`wildcard-houseflow-cloud`) et
  du compte ACME (`acme-account`). Le rôle custom « HouseFlow Deployer » a reçu les permissions
  `Microsoft.KeyVault/vaults/{read,write,delete}` et `vaults/accessPolicies/write`
  (`infrastructure/rbac/houseflow-deployer.role.json`, commande de mise à jour dans
  `specs/architecture.md`) ; le fournisseur `Microsoft.KeyVault` a dû être enregistré sur
  l'abonnement. Le provider azurerm est configuré avec `purge_soft_delete_on_destroy = false` et
  `recover_soft_deleted_key_vaults = false` : l'API `deletedVaults` est au niveau abonnement,
  hors portée d'un rôle limité au resource group.
- **`certificate.yml`** — émission `lego` (DNS-01 OVH, serveur ACME de production), import PFX
  dans Key Vault (algorithmes legacy 3DES/SHA1 obligatoires, sinon Key Vault et Container Apps
  rejettent le fichier), upload sur l'environnement `cae-houseflow`. Déclenché après un run
  `Infrastructure` réussi sur `main`, au push du workflow, le 1er du mois, ou à la main (`force`).
  Idempotent : n'émet que si le certificat manque, expire dans moins de 30 jours ou vient du
  staging ; refait l'upload à chaque run. Épinglé sur lego 5.5.1 (`lego run --accept-tos …`, la
  v5 a déplacé les options globales sous la sous-commande). Adresse de contact :
  `vars.LETSENCRYPT_EMAIL` (repli `admin@houseflow.cloud`).
- **DNS découplé des apps** — `deploy-dns-ovh` ne lit plus que `main.tfstate` : le FQDN par défaut
  d'une Container App est `<app>.<default_domain>` et l'ID de vérification est une propriété de
  l'environnement. Le DNS s'applique donc *avant* les apps (ordre `main` → DNS → `deploy-*`), ce
  qui supprime le double apply de #203. Nommage aplati : `api-preprod.houseflow.cloud` (un seul
  label, le wildcard ne couvre qu'un niveau). Le CNAME `www` exigeait la suppression, côté OVH,
  de la redirection web et du `TXT www "3|welcome"` posé par défaut (RFC 1034 : un CNAME ne
  coexiste avec rien).
- **Bindings déclaratifs** — `deploy-preprod`/`deploy-prod/custom-domains.tf` :
  `azurerm_container_app_custom_domain` en `SniEnabled` sur l'output `wildcard_certificate_id` de
  `main`. Plus de `azapi` ni de `local-exec az` : les certificats gérés `cert-*-preprod/prod` ont
  été détruits une fois les hôtes rebindés (Azure refuse de supprimer un certificat encore lié).
- **Previews de PR** (`modules/ephemeral-env`) — chaque preview crée ses enregistrements OVH
  (`api-pr-<n>` CNAME + TXT asuid, `pr-<n>` CNAME vers la Static Web App, TTL 60 s), lie
  `api-pr-<n>` au wildcard et `pr-<n>` à la Static Web App (délégation CNAME, certificat émis
  par Azure), après un `time_sleep` de 60 s de propagation. Frontend et API étant same-site, le
  cookie de refresh repasse sur `SameSite=Lax`. `pr-preview.yml` passe les credentials OVH aux
  étapes apply et destroy. Le domaine de la Static Web App est une opération longue dont Azure
  publie l'état au niveau souscription : un second rôle, « HouseFlow Deployer (subscription) »
  (`infrastructure/rbac/houseflow-deployer-subscription.role.json`, `Microsoft.Web/locations/*/read`
  seulement — l'action exacte `staticSitesOperationStatuses/read` n'étant pas publiée par le
  provider), est assigné au même service principal à l'échelle de la souscription
  (`Assign-DeployerSubscriptionRole.ps1`).
- **Workflows** — `infra.yml` : garde-fou sur le plan sauvegardé (refuse toute destruction de
  ressource protégée : environnement, PostgreSQL, Key Vault, VNet, identité, Log Analytics) et
  sur le DNS (destruction d'un enregistrement seulement avec le marqueur de commit
  `[dns-allow-destroy]` ou l'input `allow_dns_destroy`) ; `plan-dns-ovh` s'enchaîne après
  `apply`. `deploy.yml` : attente du run `Infrastructure` (puis `Certificate`) du même commit
  (`scripts/ci/wait-infrastructure.sh`, jusqu'à 2 h — la file GitHub a dépassé l'heure), plus de
  groupe de concurrence au niveau workflow (un run en attente d'approbation bloquait tous les
  suivants), réservation du tag CalVer par push-first avec retry (deux builds concurrents
  généraient le même tag).
- **Vérifié en conditions réelles** — `https://api-preprod.houseflow.cloud/health`,
  `https://preprod.houseflow.cloud/`, `https://api.houseflow.cloud/health`,
  `https://www.houseflow.cloud/` : HTTP 200, `CN=*.houseflow.cloud`, CORS restreint à l'origine
  exacte du frontend.

**Limites connues.** L'apex nu `https://houseflow.cloud` réinitialise la connexion : la
redirection OVH est en HTTP seul (hors Terraform). Les enregistrements `rouss.be` restent à
supprimer manuellement chez OVH (#208). Les certificats gérés `cert-*` ne reviendront pas : tout
nouvel hôte doit se lier au wildcard.

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

## Recent Changes (2026-09-23) — repasse complète

### Correctifs issus de la relecture en quatre axes (backend, frontend, docs, sécurité)
- **Fenêtre de grâce des refresh tokens bornée.** Le jeton frère introduit lors du merge du
  2026-09-14 neutralisait la détection de réutilisation : les deux branches ne collisionnaient
  plus jamais. Désormais **une seule grâce par jeton parent** (colonne `GraceUsedAt`, migration
  `AddRefreshTokenGraceUsedAt`), le frère **hérite de l'échéance** du jeton qu'il double, et le
  second rejeu retombe sur la révocation de famille. Le contrôle Art. 18 passe **avant** ce bloc.
- **Élévation vers administrateur fermée.** L'unicité des e-mails est désormais insensible à la
  casse à l'inscription **et** à la rectification, comme l'est déjà `AdminBootstrap`.
- **Endpoints RGPD restreints au JWT** (`AuthenticationSchemes = Bearer`) : une clé d'API ne peut
  plus déclencher l'export complet ni changer l'adresse e-mail du compte. `ValidateKeyAsync`
  refuse en outre les clés d'un compte sous limitation Art. 18.
- **`GetClientIp` ne lit plus `X-Forwarded-For`** : la preuve d'acceptation des CGU et la trace
  d'export étaient falsifiables par l'utilisateur lui-même.
- **E-mails retirés des journaux** (`AuthService` ×3, `AdminService`), conformément à ce que la
  documentation affirmait déjà.
- **`DataRetentionOptions` validé au démarrage** : une durée ou une taille de lot à 0 désactivait
  la purge en silence.
- **Migration purgeant les refresh tokens en clair** antérieurs au hachage.
- **5 chemins OpenAPI restaurés** (`/users/me`, `/users/me/export`, `/users/me/consent`), perdus
  dans une résolution de conflit sans que rien ne le signale.
- **Textes légaux corrigés** : tableau des traceurs (seul `houseflow_session` existe), durée réelle
  du cookie, sort d'une maison partagée uniquement avec des locataires, nonce CSP inexistant retiré.
  Version de politique portée à **2026-09-23**, et un test verrouille l'égalité des deux constantes.
- **Registre** : colonnes manquantes ajoutées à TR-01, **fiche TR-08** créée pour le back-office
  d'administration, réserve explicite sur les maisons préservées de `dbtools`, quatre points ouverts
  ajoutés. Sous-traitants, procédure de violation et `SECURITY.md` alignés sur la réalité.
- **Export** : le frontend n'annonce plus « téléchargé » quand le navigateur a échoué, alors que le
  quota horaire est déjà consommé. Un administrateur ne perd plus `IsAdmin` en enregistrant son profil.

## Recent Changes (2026-09-23)

### Fusion de 102 commits de `main` dans la branche RGPD
- **`scripts/sanitize-pii.sh` supprimé côté `main`**, remplacé par la chaîne `dbtools`
  (`pseudonymize.sql` + `verify.sql` bloquant + `PseudonymizationTests`). La suppression est
  acceptée : la couverture de `pseudonymize.sql` englobe strictement celle de l'ancien script.
- **`Users.ConsentPolicyVersion` classée non personnelle** dans `PseudonymizationTests` : c'est
  la seule colonne texte ajoutée par la branche, et le test refuse toute colonne non classée.
- **Fiche TR-07 du registre réécrite.** Elle décrivait une préproduction disparue et affirmait
  qu'aucune personne réelle n'était concernée. C'est faux depuis `preserved_emails` : le compte
  du mainteneur traverse la chaîne **intact** jusque dans les environnements de PR. La fiche le
  dit désormais, et pose la règle qu'ajouter le compte d'un tiers à cette liste exigerait son
  information préalable. Références corrigées dans le README RGPD, les sous-traitants, la LIA
  et la procédure de violation.
- **`verify-e2e.sh`** : la sonde d'assets obsolètes de `main` est conservée, portée sur
  `$FRONTEND_URL` au lieu du port 3000 en dur, pour rester compatible avec les worktrees.

## Recent Changes (2026-09-14)

### Fusion de la session persistante et du hachage des refresh tokens (RGPD, #132–#139)
- Les deux chantiers touchaient la même zone : `main` a introduit les **familles de jetons** (« se souvenir de moi »,
  éviction de la session la moins récemment utilisée au-delà de dix, fenêtre de grâce de 30 s pour la course entre
  onglets) pendant que la branche RGPD hachait les refresh tokens en SHA-256 (Art. 32(1)(a)). L'architecture de
  `main` est conservée, le hachage réappliqué par-dessus : `CreateRefreshToken` rend `(entité, jeton en clair)`,
  la valeur en clair ne sort que vers le cookie HttpOnly, et tous les lookups passent par `TokenHasher.Hash`.
- **Fenêtre de grâce adaptée.** `main` renvoyait à l'onglet perdant le jeton courant ; la base n'en détenant plus
  que l'empreinte, cette valeur n'est pas rejouable. L'onglet perdant reçoit désormais un **jeton frère dans la
  même famille**, sans révoquer celui de l'onglet gagnant : même intention, aucun secret conservé en clair.
- **Cookie de refresh factorisé** dans `src/HouseFlow.API/Authentication/RefreshTokenCookie.cs` : nom, chemin
  restreint `/api/v1/auth` (minimisation, Art. 25/32), `SameSite` configurable (`Auth:CookieSameSite`, `None` sur
  les previews de PR) et expiration. `AuthController` et `UsersController` (suppression de compte) s'appuient sur
  la même source, sans quoi le navigateur refuse la suppression du cookie.
- **Tests d'intégration hors devcontainer** : `POSTGRES_HOST` doit rester **non défini** (garde-fou du
  `IntegrationTestFixture`, qui refuse tout hôte autre que le sidecar `postgres` avant un `DROP DATABASE`).
  Aspire démarre alors son propre conteneur PostgreSQL ; Docker doit tourner.
## Recent Changes (2026-09-16) — Bascule prod vers `houseflow.cloud`

`infrastructure/terraform/deploy-prod` pointait encore vers `houseflow.rouss.be`. Depuis #202
(mergée), les enregistrements DNS de `www.houseflow.cloud` / `api.houseflow.cloud` existent déjà
côté OVH (pilotés par `deploy-dns-ovh`), donc la bascule ne portait que sur `deploy-prod` :

- `var.frontend_domain_prod` → `www.houseflow.cloud`, `var.api_domain_prod` → `api.houseflow.cloud`
  (le frontend est sur `www.`, pas sur l'apex nu — voir `specs/architecture.md` § DNS)
- `var.jwt_issuer` / `var.jwt_audience` alignés sur les nouveaux domaines — invalide tous les JWT
  existants en prod (déconnexion globale), accepté comme non-problème vu l'absence d'utilisateurs
  réels
- `custom-domains.tf` référence déjà les variables (`var.api_domain_prod`/`var.frontend_domain_prod`),
  aucun changement de logique nécessaire — seuls les commentaires documentant les prérequis DNS ont
  été mis à jour
- Les enregistrements DNS `rouss.be` restent en place chez OVH (hors Terraform) le temps de vérifier
  `houseflow.cloud` en prod, à supprimer manuellement ensuite (#208)

L'apply se fait automatiquement au merge sur `main` (job `deploy-prod` de `deploy.yml`, avec
approbation manuelle sur l'environnement GitHub `prod`) — ce changement ne modifie que les valeurs
par défaut des variables Terraform, pas le workflow de déploiement.

## Recent Changes (2026-09-16)

### Issue routine: close the self-retriggering loop on `claude`-labelled issues

Issue #204 fired six automated sessions in thirteen minutes. Each one found the same blocker (#204
depends on #202, not yet merged), posted a comment saying so, and that comment re-triggered
`claude-issue.yml` — the routine comments under the repo owner's GitHub identity, so the workflow
could only tell it apart from a human reply by the `<!-- claude-routine:auto -->` marker, which
those comments didn't carry. The loop stopped only when the `claude` label was removed by hand.

- **Root cause** — the marker requirement lived in the workflow's header comment, in
  PROJECT_KNOWLEDGE.md and in the routine's own prompt (hosted outside the repo), but not in
  `CLAUDE.md`, the one file every session reads before commenting. It is now a rule of its own in
  the Taxonomy section, restated inline at the two points where a session comments (Phase 2 §1
  question-when-blocked, Phase 3 §6 final report).
- **Defence in depth** — `claude-issue.yml` no longer relies on the agent's cooperation alone: the
  `issue_comment` job also skips comments containing the `Generated by [Claude Code]` attribution
  footer, which the agent appends to every GitHub comment it writes.

Not covered here: the `claude` label is still not removed automatically when a session stops
blocked, so a blocked issue stays armed for the next manual re-label.

## Recent Changes (2026-09-14)

### Issue-routine prompt: ask on the issue when info is missing, branch naming tied to the issue

Two gaps in the `claude`-label routine ("Correction issue HouseFlow", `trig_017zpGmaCX8P9nKNdqqkni8h`)
fixed after first real-world use:

- The routine's prompt now explicitly tells it to post clarifying questions as a comment on the
  issue (`gh issue comment`) and stop without coding when something needed to implement safely is
  missing or ambiguous — rather than guessing or silently doing nothing. A later run re-reads the
  issue's comments and can pick up an answer given in the meantime.
- Branches it creates are now named `claude/issue-<n>-<short-kebab-case-summary>` (e.g.
  `claude/issue-201-fix-login-redirect`) instead of the default random `claude/<adjective>-<name>`,
  so a branch is identifiable from its issue at a glance. `CLAUDE.md` Phase 2 documents the same
  convention for interactive sessions, plus the interactive-vs-automated split for when to ask the
  user directly versus commenting on the issue.
- The routine also now posts a final report comment on the issue when done (PR link, brief
  summary, CI state) and is told to keep its own conversational output minimal — GitHub comments
  (questions, blockers, final report) are the primary channel, not the session transcript.
  `CLAUDE.md` Phase 3 documents this "automated session only" step alongside the interactive flow.
- `claude-issue.yml` also triggers on `issue_comment: created`, so replying to Claude's question
  on the issue starts a **new** session (routines don't resume a prior one) that re-reads the
  issue and picks up from the answer. To avoid an infinite loop — a routine's comments post under
  the routine owner's own GitHub identity, indistinguishable from a human reply by author alone —
  every automated comment (the workflow's "session started" ping and the routine's own questions/
  blockers/final report) is prefixed with the `<!-- claude-routine:auto -->` marker. Since the
  marker depends on the session remembering to add it — and #204 showed it doesn't always — the
  trigger treats the `Generated by [Claude Code]` attribution footer as a second automated-comment
  signal. The `issue_comment` trigger only fires when the new comment carries neither signal, the
  issue is still open, still carries the `claude` label, and isn't a PR comment. Caveat: answering
  Claude with GitHub's "Quote reply" copies the footer into the reply and will be ignored — reply
  without quoting, or re-apply the `claude` label.
- The routine is edited from the routines web UI (`update_trigger` refuses routines not created by
  an agent's own `create_trigger` call — this one was created via the web UI/API directly), so its
  prompt is kept in sync with `CLAUDE.md` by hand going forward.

## Recent Changes (2026-09-12)

### Session persistante « Se souvenir de moi », JWT en mémoire, détection de réutilisation (#164)
- Le JWT n'est plus écrit dans `localStorage` (ni le profil dans `sessionStorage`) : `TokenStore` est purement
  en mémoire et `App.razor` échange le cookie HttpOnly contre un access token à chaque démarrage. Avant, fermer le
  navigateur perdait le profil (`sessionStorage`) mais gardait le JWT sur disque → reconnexion forcée sans gain de sécurité.
- Case « Se souvenir de moi » sur `/login` (`LoginRequest.rememberMe`, `specs/openapi.yaml`) : cookie persistant
  365 jours glissants ; sinon cookie de session (24 h serveur).
- `RefreshTokens` : colonnes `FamilyId` (indexée) et `RememberMe` (migration `20260912192157_AddRefreshTokenFamilyAndRememberMe`,
  backfill d'une famille par token existant). Détection de réutilisation par famille avec grâce de 30 s, 10 sessions max
  par utilisateur (éviction LRU), purge des tokens révoqués après 7 jours. Détails : « Sessions » ci-dessus.
- Au démarrage, le refresh n'est tenté que si un indice de session (`localStorage` `houseflow_session`) existe, avec le
  splash affiché et un délai max de 45 s : l'API de preview (0 réplica au repos, ~30 s de démarrage à froid) laissait
  une page blanche à tout visiteur, même déconnecté.
- Attribut `SameSite` du cookie configurable (`Auth:CookieSameSite`, Lax par défaut) et positionné à `None` sur les previews
  PR (frontend et API sur deux sites) ainsi que sur l'API des E2E, dont un scénario pilote le frontend depuis `127.0.0.1:3000`
  pour reproduire ce cas cross-site (#196).
- E2E : plus d'injection de token dans `localStorage` ; les tests posent le cookie `refreshToken` dans le contexte
  Playwright (`e2e/fixtures/auth.ts` : `refreshCookieFrom` / `addRefreshCookie`, qui pose aussi l'indice de session).
  Nouvelle spec `session-persistence.spec.ts`.
  Attention : `page.request` partage le cookie jar du navigateur — préparer les données avec la fixture `request` (isolée)
  quand le test doit ensuite voir la page de login.
- Les shims `window.__setAccessToken` / `__INITIAL_AUTH_TOKEN` et les helpers `hf.local*`/`hf.session*` de `app.js` sont supprimés.

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
1. **Auto-Create First House**: On registration, creates "Ma Maison" *(removed 2026-09-27 — onboarding P05 creates it)*
2. **Single House Auto-Redirect**: Users with 1 house are redirected to house details *(no longer true)*
3. **Optional Address Fields**: Address, zipCode, city no longer required
4. **Device Creation Flow**: Redirects to device creation after registration *(replaced 2026-09-27 by the P05 → P06 onboarding)*

## Known Issues

- English copy of the landing page (`landing.title` / `landing.subtitle`) and the invitation wording (« Créer
  l'invitation », « Invitation en attente » — no email is sent) await a product review.

## File Locations

### Configuration
- OpenAPI Spec: `specs/openapi.yaml`
- Tailwind Config (v4, CSS-first — no `tailwind.config.js`): `src/HouseFlow.Web/Styles/app.input.css` (`@theme inline`, `@source`, `@plugin`) → output `src/HouseFlow.Web/wwwroot/css/app.css`
- i18n Messages: `src/HouseFlow.Web/Localization/Resources/{fr,en}.json`
- Rider Run Configs: `.idea/.idea.HouseFlow/.idea/runConfigurations/`

### Key Backend Files
- RGPD: `src/HouseFlow.Application/Common/{GdprPolicy,IpAddressAnonymizer,TokenHasher,DataRetentionOptions,CsvExportWriter}.cs`, `Services/{UserAccountService,ConsentService}.cs`, `src/HouseFlow.Infrastructure/Jobs/DataRetentionJob.cs`, `src/HouseFlow.API/Controllers/{UsersController,ConsentController}.cs`
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
- Pages (by feature): `src/HouseFlow.Web/Features/` (Admin, Auth, Dashboard, Devices, Houses, Invitations, Legal, Settings, Setup, Shared)
- Layouts: `src/HouseFlow.Web/Layout/` (`MainLayout`, `AuthLayout`, `SetupLayout`, `DashboardLayout`, `AppShell`, `ErrorLayout`, `ProtectedLayoutBase`)
- Shared Components: `src/HouseFlow.Web/Components/`
- Services / rules: `src/HouseFlow.Web/Services/` (`ToastService`, `NavCounterService`, `SessionService`, `AppIconService`), `src/HouseFlow.Web/Rules/` (`ParisClock`, `DueStatus` = `StatusRules`/`DateFormatter`/`MoneyFormatter`, `PeriodicityRules`, `InitialDueRules`)
- Styles (Tailwind source + design tokens): `src/HouseFlow.Web/Styles/app.input.css`
- Static assets: `wwwroot/fonts/` (self-hosted woff2), `wwwroot/icons/` (app icon / favicon by status), `wwwroot/manifest.webmanifest`
- Auth: `src/HouseFlow.Web/Auth/` (`TokenStore`, `AppAuthStateProvider`, `AuthMessageHandler`, `RedirectGuard`, `AppRoutes`)
- Localization: `src/HouseFlow.Web/Localization/` (`Localizer`, `LocalizationState`, `Resources/{fr,en}.json`)
- Runtime config: `src/HouseFlow.Web/wwwroot/appsettings.json` → `AppConfig.cs`

### Frontend Structure
```
src/HouseFlow.Web/
├── Api/                   # ApiService (HttpClient wrapper), DTOs, RetryState
├── Auth/                  # TokenStore, AppAuthStateProvider, AuthMessageHandler, RedirectGuard, AppRoutes
├── Components/            # Shared Razor components (C1–C8, Modal, ConfirmDialog, MaintenanceRow, AsyncSection, ...)
├── Features/              # Routable page components, grouped by area
│   ├── Admin/            # AdminPage (P12)
│   ├── Auth/             # Login (P02), Register (P03)
│   ├── Dashboard/        # Dashboard (P07)
│   ├── Devices/          # DeviceDetailPage (P10), DeviceModal (M2)
│   ├── Houses/           # HousesPage (P08), HouseDetailPage (P09), HouseModal (M1), MembersModal (M5)
│   ├── Invitations/      # AcceptInvitation (P04)
│   ├── Legal/            # LegalPage, PrivacyPolicy (P14), TermsOfService (P15) (+ FR/EN content components, frozen)
│   ├── Settings/         # Settings (P11) + Profile/Data/ApiKeys/DeleteAccount sections
│   ├── Setup/            # SetupHouse (P05), SetupDevices (P06)
│   └── Shared/           # Landing (P01), NotFoundPage (P13), DemoData, DeviceCatalog
├── Layout/               # MainLayout, AuthLayout, SetupLayout, DashboardLayout, AppShell, ErrorLayout, ProtectedLayoutBase
├── Localization/         # Localizer, LocalizationState, Resources/{fr,en}.json
├── Rules/                # ParisClock, DueStatus (StatusRules, DateFormatter, MoneyFormatter), PeriodicityRules, InitialDueRules
├── Services/             # ToastService, NavCounterService, SessionService, AppIconService
├── Styles/               # app.input.css (Tailwind source)
├── wwwroot/              # compiled css/app.css, js/app.js, fonts/, icons/, manifest.webmanifest, appsettings.json
└── App.razor, Program.cs, _Imports.razor, ThemeService.cs, AppConfig.cs

e2e/                       # (repo root) Playwright E2E
├── fixtures/             # Playwright fixtures (auth, maintenance-seed, db)
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
bash scripts/dev-web.sh start     # frontend (HouseFlow.WebHost) on :3000
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
- **Maquettes UX**: `specs/ux/` (`README.md` = handoff hi-fi, référence visuelle unique)
- **Task Management**: [GitHub Issues](https://github.com/BarbeRouss/HouseFlow/issues) + [Milestones](https://github.com/BarbeRouss/HouseFlow/milestones)
- **Lessons Learned**: `tasks/lessons.md`

---

**Note**: This is a living document. Update it whenever significant changes are made to the project.
