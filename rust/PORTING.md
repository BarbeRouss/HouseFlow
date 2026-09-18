# HouseFlow backend — portage Rust (spec de travail)

Ce document est le contrat que suivent tous les agents qui écrivent le backend Rust.
Objectif : une **copie fonctionnelle complète** du backend .NET (`src/HouseFlow.API` +
`HouseFlow.Application` + `HouseFlow.Infrastructure`) en Rust, qui **passe la même suite de
tests d'intégration** (`tests/HouseFlow.IntegrationTests`, exécutée en boîte noire HTTP contre
le serveur Rust), et qui tourne **uniquement dans le devcontainer** (pas de déploiement).

Source de vérité fonctionnelle : le code C# existant. En cas de doute, **lire le C# et les tests
d'intégration** — pas deviner. `specs/openapi.yaml` décrit le contrat mais les tests font foi.

## 1. Décisions d'architecture (non négociables)

| Sujet | Décision |
|---|---|
| Emplacement | Workspace Cargo `rust/` (`rust/Cargo.toml`), crate binaire `rust/houseflow-api` |
| Runtime / HTTP | `tokio` (full) + `axum` 0.8 + `tower-http` (cors, trace, set-header) |
| DB | `sqlx` 0.8, features `runtime-tokio`, `tls-none` (ou `tls-rustls`), `postgres`, `uuid`, `chrono`, `rust_decimal`, `json`, `migrate`. **Pas de macros `query!` vérifiées à la compilation** (pas de `DATABASE_URL` requis au build, pas de `.sqlx/` offline) : utiliser `sqlx::query` / `query_as::<_, T>` avec `#[derive(FromRow)]` |
| Migrations | `sqlx::migrate!()` embarqué, fichiers dans `rust/houseflow-api/migrations/`. La 1ʳᵉ migration reproduit **exactement** le DDL de la base .NET (tables, colonnes, types, defaults, index, FK, ON DELETE) — référence : `pg_dump` de la base migrée par EF, fichier `/tmp/claude-0/-home-user-HouseFlow/c25f57c6-e7bd-548b-bb10-cff799d0be98/scratchpad/dotnet-schema.sql` (à défaut : régénérer avec `dotnet run --project src/HouseFlow.API -- --migrate` puis `pg_dump --schema-only`). Ne PAS créer `__EFMigrationsHistory`. Noms de tables/colonnes **PascalCase entre guillemets** comme EF (`"Users"."FirstName"`) — même schéma ⇒ comparaison de perf équitable et base interchangeable |
| Base de données | Même serveur Postgres que .NET, **base dédiée** : `houseflow_rust` (dev), `houseflow_rust_test` (tests). Au démarrage, si `AUTO_MIGRATE=true` (défaut en dev) : créer la base si absente (connexion à la base `postgres` puis `CREATE DATABASE`) puis appliquer les migrations — équivalent de `Database.Migrate()` |
| JSON | `serde` + `serde_json`. Tous les DTO en `#[serde(rename_all = "camelCase")]`. Enums sérialisés en **chaînes PascalCase** (`"Owner"`, `"ReadWrite"`, `"Pending"`) et désérialisés **insensibles à la casse** (comme `JsonStringEnumConverter`). Dates : `chrono::DateTime<Utc>` en RFC 3339 avec `Z`. Décimaux (`Cost`) : `rust_decimal` avec feature `serde-with-float` (nombre JSON, pas chaîne) |
| Mots de passe | `bcrypt` crate, **coût 11** (défaut de BCrypt.Net) — hashes `$2a$`/`$2b$` interchangeables avec .NET |
| JWT | `jsonwebtoken`, HS256, mêmes claims que .NET : `sub` (user id), `email`, `jti`, `iss`, `aud`, `exp` (15 min), et pour les admins `http://schemas.microsoft.com/ws/2008/06/identity/claims/role` = `"Admin"`. `ClockSkew = 0`. Un JWT émis par un backend doit être accepté par l'autre (même clé) |
| Clés API | Identique à `ApiKeyService.cs` : préfixe `hf_`, corps base62 de 32 octets, `Prefix` = 11 premiers chars, `KeyHash` = SHA-256 hex minuscule de la clé complète, max 5 actives/utilisateur, `LastUsedAt` mis à jour à chaque validation |
| Sélection auth | Header `X-API-Key` **ou** `Authorization: Bearer hf_…` ⇒ clé API ; sinon `Authorization: Bearer <jwt>`. L'identité clé API ne porte **jamais** le rôle admin |
| Scope clé API | POST/PUT/DELETE/PATCH avec une clé `ReadOnly` ⇒ **403** `{ "error": "This API key has read-only access. A ReadWrite key is required for this operation." }` — sauf sur `/api/v1/users/api-keys*` |
| Erreurs | Reproduire les statuts et corps du C# : `KeyNotFoundException` ⇒ 404 `{ "error": msg }` ; `InvalidOperationException` ⇒ 400 `{ "error": msg }` (sauf cas 409 « already registered » du register) ; `UnauthorizedAccessException` dans un service ⇒ **403 corps vide** (ForbidResult) ; sur `/auth/login`/`/auth/refresh` ⇒ 401 `{ "error": msg }` ; auth manquante/invalide ⇒ **401 corps vide** + `WWW-Authenticate: Bearer` ; rôle admin manquant ⇒ 403 vide |
| Validation | Reproduire les `DataAnnotations` des DTO générés (`src/HouseFlow.Application/Generated/Contracts.g.cs` + `ContractValidation.cs`) : `[Required]`, `[StringLength]`, `[Range]`, `[RegularExpression]`, `[NotInFuture]`, format e-mail. Échec ⇒ **400** au format ProblemDetails ASP.NET : `{ "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1", "title": "One or more validation errors occurred.", "status": 400, "errors": { "Email": ["..."] } }` (clés = nom de propriété PascalCase). JSON malformé / champ requis manquant ⇒ 400 aussi. Corps non-JSON ⇒ 415 |
| Routes GUID | `{id:guid}` (contrainte de route) ⇒ GUID invalide = **404** ; paramètre `Guid` sans contrainte (`{houseId}`) ⇒ GUID invalide = **400** ProblemDetails. Vérifier dans les contrôleurs C# lequel s'applique par route |
| Cookies | Refresh token dans cookie `refreshToken` HttpOnly, `Path=/`, `SameSite` configurable (`AUTH__COOKIE_SAME_SITE`, défaut `Lax`), `Secure` si HTTPS ou SameSite=None, `Expires` seulement si rememberMe. Logout/revoke : suppression avec les **mêmes attributs** (expires=epoch) |
| Audit | Chaque mutation écrit dans `"AuditLogs"` (même transaction) : `EntityType` (nom C# de l'entité : `User`, `House`, `RefreshToken`…), `EntityId`, `Action` (`Added`/`Modified`/`Deleted`), `UserId`, `Username`, `Timestamp`, `IpAddress`, `UserAgent`, `NewValues`/`OldValues`/`ChangedProperties` (JSON, comme `HouseFlowDbContext.OnBeforeSaveChanges`). Implémenter un helper `audit::record(&mut tx, ...)` et l'appeler partout où le C# fait un `SaveChangesAsync` avec des changements |
| Soft delete | **Aucune** entité n'implémente réellement `ISoftDeletable` (aucune colonne `IsDeleted` en base) : les DELETE sont des vrais DELETE (cascades FK identiques à EF) |
| Rate limiting | .NET ne l'active qu'en Production/Staging ⇒ **non porté** (documenter) |
| Job Hangfire | `CleanupExpiredInvitationsJob` ⇒ tâche `tokio` périodique (toutes les 24 h, 1ʳᵉ exécution au démarrage), même logique |
| Health | `GET /health` (vérifie `SELECT 1`) et `GET /alive` ⇒ 200 `Healthy` (text/plain) |
| Swagger | `GET /swagger/index.html` ⇒ 200 (page minimale) et `GET /swagger/v1/swagger.json` ⇒ contenu de `specs/openapi.yaml` converti/servi (utoipa est optionnel ; le minimum est que `/swagger/index.html` réponde 200 — les scripts s'en servent comme readiness probe) |
| Headers sécurité | Comme `SecurityHeadersMiddleware.cs` sur toutes les réponses |
| CORS | `CORS__ORIGINS` (liste séparée par virgules, défaut `http://localhost:3000,https://localhost:3000`, `*` = toutes), méthodes GET/POST/PUT/DELETE, headers `Authorization`, `Content-Type`, credentials |
| Logs | `tracing` + `tracing-subscriber` (env filter, défaut `info`), format compact sur stdout |
| Seeds | En dev (`APP_ENV=Development`) : utilisateur `admin@admin.com` / `admin` si absent. Si `DEMO_MODE=true` : `demo@demo.com` / `Demo@2026!` admin + maison « Ma maison » + membership Owner. Au démarrage (tous env) : promouvoir admin les comptes existants listés dans `ADMIN__BOOTSTRAP_EMAILS` |

### Configuration (variables d'environnement)

| Variable | Défaut | Rôle |
|---|---|---|
| `DATABASE_URL` | `postgres://postgres:postgres@localhost:5432/houseflow_rust` | Connexion (si `POSTGRES_HOST` est défini et `DATABASE_URL` absent, host = `$POSTGRES_HOST`) |
| `PORT` | `5204` | Port d'écoute, bind `0.0.0.0` |
| `APP_ENV` | `Development` | `Development` ⇒ auto-migrate + seed admin ; autre ⇒ pas de seed |
| `AUTO_MIGRATE` | `true` | Créer la base + appliquer les migrations au démarrage |
| `JWT__KEY` | *(obligatoire, ≥ 32 chars)* | Clé HS256 — même valeur que .NET (`appsettings.Development.json` : `DevOnlySecretKey_DO_NOT_USE_IN_PRODUCTION_MinimumLengthRequired256Bits!`) |
| `JWT__ISSUER` / `JWT__AUDIENCE` | `HouseFlowAPI` / `HouseFlowClient` | |
| `ADMIN__BOOTSTRAP_EMAILS` | `julienrousselle@outlook.be` | Liste séparée par virgules (même valeur que `appsettings.json`) ; accepter aussi `Admin__BootstrapEmails__N` |
| `DEMO_MODE` | `false` | |
| `CORS__ORIGINS` | voir ci-dessus | |
| `AUTH__COOKIE_SAME_SITE` | `Lax` | `Lax`/`None`/`Strict` ; accepter aussi `Auth__CookieSameSite` |
| `RUST_LOG` | `info` | |

Le crate expose aussi `--migrate` (appliquer les migrations puis quitter), comme .NET.

## 2. Structure du crate

```
rust/
├── Cargo.toml                  # workspace
├── PORTING.md                  # ce document
└── houseflow-api/
    ├── Cargo.toml
    ├── migrations/0001_initial.sql
    └── src/
        ├── main.rs             # config, pool, migrations, seeds, router, job, serve
        ├── config.rs
        ├── db.rs               # pool, création de base, migrate
        ├── error.rs            # AppError → réponse HTTP (voir tableau Erreurs)
        ├── audit.rs
        ├── auth/               # jwt.rs, api_key.rs, extractor.rs (CurrentUser), scope.rs
        ├── models/             # structs FromRow par table + enums (HouseRole, InvitationStatus, ApiKeyScope, …)
        ├── dto/                # DTO requête/réponse (miroir de Contracts.g.cs + DTOs/*.cs) + validation
        ├── services/           # logique métier, 1 fichier par service C# (auth, houses, members, devices, maintenance, calculator, api_keys, admin, user_settings)
        ├── routes/             # handlers axum, 1 fichier par contrôleur C#, + mod.rs qui assemble le Router
        ├── middleware/         # security_headers.rs, audit_context.rs
        └── jobs/cleanup_expired_invitations.rs
```

Règle : un handler = validation + appel service + mapping réponse. La logique reste dans
`services/` (testable unitairement). Les services prennent `&PgPool` ou une transaction.

## 3. Périmètre fonctionnel (tout doit exister)

Contrôleurs C# ⇒ routes (toutes sous `/api/v1`) :

- **AuthController** : `POST auth/register?invitationToken=`, `POST auth/login`, `POST auth/refresh`, `POST auth/revoke` (auth), `POST auth/logout` (auth)
- **UserSettingsController** : `GET/PUT users/settings`
- **ApiKeysController** : `POST/GET users/api-keys`, `DELETE users/api-keys/{id:guid}`
- **HousesController** : `GET/POST houses`, `GET/PUT/DELETE houses/{houseId}`
- **DevicesController** : `GET/POST houses/{houseId}/devices`, `GET/PUT/DELETE devices/{deviceId}`, `GET/POST devices/{deviceId}/maintenance-types`, `GET devices/{deviceId}/maintenance-history`
- **MaintenanceController** : `PUT/DELETE maintenance-types/{typeId}`, `POST maintenance-types/{typeId}/instances`, `GET upcoming-tasks`, `PUT/DELETE maintenance-instances/{instanceId}`
- **MembersController** : `GET collaborators`, `GET houses/{houseId}/members`, `PUT members/{memberId}/role`, `PUT members/{memberId}/permissions`, `DELETE members/{memberId}`, `POST/GET houses/{houseId}/invitations`, `GET invitations/{token}` (anonyme), `POST invitations/{token}/accept`, `DELETE invitations/{invitationId}`
- **AdminController** (rôle Admin, JWT seulement) : `GET admin/stats`, `GET admin/users`, `PUT admin/users/{id:guid}/admin`

Services C# à porter fidèlement : `AuthService`, `HouseMemberService` (RBAC : Owner/Member,
`CanLogMaintenance`, vérifications d'accès utilisées par tous les autres services), `HouseService`,
`DeviceService`, `MaintenanceService`, `MaintenanceCalculatorService` (scores, statuts, prochaines
échéances — logique pure), `ApiKeyService`, `AdminService`, `UserSettingsService`, `AdminBootstrap`.

## 4. Tests

1. **Tests unitaires Rust** (`cargo test`) : portage des tests de `tests/HouseFlow.UnitTests`
   (calculateur de maintenance, `NotInFuture`, `AdminBootstrap`, logique de rotation des refresh
   tokens) — mêmes cas, mêmes valeurs attendues.
2. **Suite d'intégration .NET en boîte noire** : `tests/HouseFlow.IntegrationTests` est purement
   HTTP. Avec `HOUSEFLOW_API_BASE_URL=http://localhost:<port>` le fixture ne démarre pas Aspire et
   cible ce serveur. C'est **le critère d'acceptation principal** : la suite complète doit passer
   contre le serveur Rust. Exécution :
   ```bash
   export PATH="/usr/share/dotnet:$PATH" DOTNET_ROOT=/usr/share/dotnet   # sur cette machine
   HOUSEFLOW_API_BASE_URL=http://localhost:5204 dotnet test tests/HouseFlow.IntegrationTests --no-build \
     --filter "FullyQualifiedName~Authentication"     # ou sans filtre pour tout
   ```
   Le serveur Rust doit avoir été lancé sur une base **fraîche** (`houseflow_rust_test` droppée
   avant) avec `ADMIN__BOOTSTRAP_EMAILS=julienrousselle@outlook.be`, `APP_ENV=Development`,
   `DEMO_MODE=false` — c'est l'équivalent de ce qu'Aspire fait pour .NET.
   Un test qui échoue contre Rust mais passe contre .NET est un bug du port, sauf si le test
   dépend d'un détail purement .NET (documenter alors l'adaptation, minimale, dans le test).
3. **E2E Playwright** (`e2e/`) contre le frontend Blazor pointé sur le backend Rust — phase finale.

## 5. Hygiène pour les agents en parallèle

- Chaque agent travaille dans **sa worktree** ; ne touche qu'aux fichiers de son périmètre ;
  n'édite `main.rs`/`routes/mod.rs` que pour **enregistrer** ses routes (petites diffs, faciles à
  fusionner).
- Pour tester manuellement, utiliser une base et un port **uniques** :
  `DATABASE_URL=postgres://postgres:postgres@localhost:5432/houseflow_rust_<agent> PORT=52xx`.
  Postgres local : `localhost:5432`, user `postgres` / `postgres`.
- `cargo fmt` + `cargo clippy --all-targets -- -D warnings` propres avant de rendre.
- Ne pas committer de `target/`. Pas de secrets nouveaux (la clé JWT de dev est déjà publique dans
  `appsettings.Development.json`).
