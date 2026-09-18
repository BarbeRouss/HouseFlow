# HouseFlow — backend Rust (`houseflow-api`)

Second backend, écrit en Rust, à **parité fonctionnelle complète** avec le backend .NET
(`src/HouseFlow.API`) : mêmes 39 endpoints, même schéma PostgreSQL, mêmes JWT et hashes de mot
de passe (interchangeables entre les deux). Objectif : comparer les deux runtimes, pas remplacer
.NET — ce crate tourne **uniquement dans le devcontainer**, jamais déployé.

Spec de travail, décisions d'architecture non négociables et périmètre fonctionnel détaillé :
**[`PORTING.md`](./PORTING.md)** — à lire avant toute modification de ce crate.

## Structure

```
rust/
├── Cargo.toml                  # workspace
├── PORTING.md                  # spec / décisions d'architecture
└── houseflow-api/
    ├── migrations/0001_initial.sql   # reproduit exactement le DDL EF (10 tables, 20 index, 11 FK)
    └── src/
        ├── main.rs             # config, pool, migrations, seeds, routeur, job, écoute
        ├── config.rs           # variables d'environnement (miroir des appsettings .NET)
        ├── auth/                # jwt, api_key, extractor, scope
        ├── models/              # structs FromRow + enums
        ├── dto/                 # DTO requête/réponse + validation
        ├── services/            # logique métier, un fichier par service C#
        ├── routes/              # handlers axum, un fichier par contrôleur C#
        ├── middleware/          # security_headers, contexte d'audit
        └── jobs/                # tâche tokio périodique (remplace Hangfire)
```

## Build / run / test

Toutes ces commandes s'exécutent **dans le devcontainer**, via `scripts/feature-env.sh` depuis
une worktree (voir `.devcontainer/README.md` § « Backend Rust ») :

```bash
scripts/feature-env.sh exec <worktree> -- bash scripts/rust-api.sh build  # cargo build --release
scripts/feature-env.sh exec <worktree> -- bash scripts/rust-api.sh start  # démarre sur :5204 (base houseflow_rust)
scripts/feature-env.sh exec <worktree> -- bash scripts/rust-api.sh stop
scripts/feature-env.sh exec <worktree> -- bash scripts/rust-api.sh unit   # cargo test + clippy -D warnings + fmt --check
scripts/feature-env.sh exec <worktree> -- bash scripts/rust-api.sh test   # suite .NET rejouée en boîte noire contre Rust
```

Le backend Rust utilise deux bases dédiées sur le même serveur PostgreSQL que .NET — jamais
`houseflow`/`houseflow_test` (réservées au backend .NET) :

| Usage | Port | Base |
|---|---|---|
| Dev interactif | 5204 | `houseflow_rust` |
| Tests boîte noire | 5214 | `houseflow_rust_test` (recréée à chaque run) |

`scripts/rust-api.sh test` rejoue **sans modification** la suite d'intégration .NET
(`tests/HouseFlow.IntegrationTests`), pointée sur le binaire Rust via
`HOUSEFLOW_API_BASE_URL=http://localhost:5214` (nouveau mode boîte noire de
`IntegrationTestFixture`) : 164/164 tests passent contre Rust comme contre .NET. Un test qui
échoue contre Rust seulement est un bug du port, à corriger là, jamais dans le test .NET (sauf
adaptation documentée, voir `PORTING.md` § 4).

Pour rejouer les E2E Playwright avec le frontend Blazor pointé sur ce backend au lieu de .NET :

```bash
HOUSEFLOW_BACKEND=rust bash scripts/verify-e2e.sh
```

## Variables d'environnement

Les noms reprennent la convention `__` de .NET pour que les deux backends tournent avec le même
environnement (copié de `PORTING.md`) :

| Variable | Défaut | Rôle |
|---|---|---|
| `DATABASE_URL` | `postgres://postgres:postgres@localhost:5432/houseflow_rust` | Connexion (si `POSTGRES_HOST` est défini et `DATABASE_URL` absent, host = `$POSTGRES_HOST`) |
| `PORT` | `5204` | Port d'écoute, bind `0.0.0.0` |
| `APP_ENV` | `Development` | `Development` ⇒ auto-migrate + seed admin ; autre ⇒ pas de seed |
| `AUTO_MIGRATE` | `true` | Créer la base + appliquer les migrations au démarrage |
| `JWT__KEY` | *(obligatoire, ≥ 32 chars)* | Clé HS256 — même valeur que .NET (`appsettings.Development.json` : `DevOnlySecretKey_DO_NOT_USE_IN_PRODUCTION_MinimumLengthRequired256Bits!`) |
| `JWT__ISSUER` / `JWT__AUDIENCE` | `HouseFlowAPI` / `HouseFlowClient` | |
| `ADMIN__BOOTSTRAP_EMAILS` | `julienrousselle@outlook.be` | Liste séparée par virgules (même valeur que `appsettings.json`) ; accepte aussi `Admin__BootstrapEmails__N` |
| `DEMO_MODE` | `false` | |
| `CORS__ORIGINS` | `http://localhost:3000,https://localhost:3000` | `*` = toutes les origines |
| `AUTH__COOKIE_SAME_SITE` | `Lax` | `Lax`/`None`/`Strict` ; accepte aussi `Auth__CookieSameSite` |
| `RUST_LOG` | `info` | |

Le binaire expose aussi `--migrate` (appliquer les migrations puis quitter), comme .NET.

## Aller plus loin

- **Spec complète** (décisions d'architecture, périmètre fonctionnel, plan de tests) :
  [`PORTING.md`](./PORTING.md)
- **Banc de perf Rust vs .NET** : `../bench/` (harnais) → rapport commité dans
  `../docs/perf/rust-vs-dotnet.md`
- **Devcontainer** : `../.devcontainer/README.md` § « Backend Rust »
