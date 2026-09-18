# DevContainer HouseFlow

Ce devcontainer sert à faire tourner l'app complète (API + Frontend + Postgres) pour du dev interactif et l'exécution de tests, sans polluer la machine locale. **Chaque worktree/feature obtient son propre projet Docker Compose** (son propre `app`, son propre `postgres`, son propre réseau) — pas de port ni de base à négocier entre sessions parallèles, chaque instance est simplement indépendante des autres.

**Claude Code ne tourne jamais à l'intérieur de ce conteneur.** Le pilotage (remote-control ou terminal) se fait depuis l'extérieur — l'hôte ou une distro Linux dédiée — qui appelle `scripts/feature-env.sh` pour démarrer/arrêter le conteneur et y exécuter des commandes (build, tests, E2E). Ça évite le docker-outside-of-docker et garde l'authentification Claude Code dans un seul endroit persistant, indépendant du cycle de vie de ce conteneur.

## Architecture

Deux services docker-compose (`.devcontainer/docker-compose.yml`), le fichier est identique dans chaque worktree (c'est un checkout complet) :

- **`app`** : conteneur principal (SDK .NET 10 + workload Aspire, Node 20). Publie les ports 3000 et 5203 vers l'hôte, mais **sans fixer le port hôte** — Docker choisit un port libre à chaque démarrage (voir `scripts/feature-env.sh url`).
- **`postgres`** : `postgres:17-alpine`, uniquement sur le réseau interne du projet (jamais publié sur l'hôte), joignable depuis `app` via l'hôte `postgres:5432`.

L'AppHost (`src/HouseFlow.AppHost/Program.cs`) détecte la présence de `POSTGRES_HOST` (injectée via `containerEnv`) et se connecte directement au conteneur `postgres` du même projet, plutôt que de demander à Aspire de spawner le sien via le socket Docker de l'hôte — ce dernier aurait été un conteneur frère non joignable simplement en `localhost` depuis le devcontainer. « Sidecar » ici désigne juste ce couple app+postgres au sein d'*un même* projet Compose — **pas** un serveur partagé entre plusieurs worktrees, ce modèle-là a été abandonné (voir plus haut). La base s'appelle toujours `houseflow` (pas de suffixe par worktree : chaque worktree a son propre serveur Postgres, donc rien à distinguer).

## Prérequis

- Docker Desktop installé et en cours d'exécution
- `jq` disponible sur la machine qui pilote `scripts/feature-env.sh` (pas dans le conteneur — sur l'hôte)

### Sur Windows

`feature-env.sh` est un script bash — sur Windows, Claude Code l'exécute via **Git for Windows** (Git Bash), pas PowerShell. C'est déjà un prérequis pour Claude Code lui-même (voir la note dans le setup initial) ; sans ça, Claude retombe sur PowerShell et ne peut pas lancer ce script du tout.

- `jq` n'est pas installé par défaut sur Windows : `winget install jqlang.jq` (ou `choco install jq`)
- Docker Desktop ajoute normalement `docker` au PATH système, donc Git Bash le trouve sans configuration supplémentaire
- Le repo a un `.gitattributes` qui force les fins de ligne LF sur les `.sh` — sans ça, un `core.autocrlf=true` (réglage par défaut de l'installeur Git pour Windows) aurait converti le script en CRLF au checkout et cassé son exécution dans Git Bash (`bad interpreter` / erreurs `\r`)

## Derrière un proxy TLS intercepteur (Claude Code web / proxy d'entreprise)

Certains environnements (notamment **Claude Code sur le web**) font sortir tout le
HTTPS par un proxy d'egress qui **re-termine le TLS** avec sa propre autorité de
certification (CA). Un conteneur de build « nu » ne connaît pas cette CA : chaque
`curl`/`apt`/`dotnet` du Dockerfile échoue alors la vérification TLS (`curl failed
to verify the legitimacy of the server`). Le devcontainer gère ce cas
**automatiquement**, sans configuration, et de façon **transparente en local** :

- `scripts/feature-env.sh` dépose la CA du proxy (`/root/.ccr/ca-bundle.crt`, ou
  `$CCR_CA_BUNDLE`) dans le contexte de build sous `.devcontainer/proxy-ca.crt`
  (gitignoré). En l'absence de proxy, il y dépose un fichier **VIDE**.
- Le `Dockerfile` copie ce fichier et, s'il est non vide, l'installe dans le trust
  store (`update-ca-certificates`) **avant le premier téléchargement HTTPS**, et
  pointe Node dessus (`NODE_EXTRA_CA_CERTS`). Fichier vide ⇒ étape **no-op** : le
  build local est strictement identique à avant.

Quand l'hôte tourne en **root** (uid 0, cas des sessions web), le conteneur reste
root pour garder le bind mount `/workspace` inscriptible : `feature-env.sh` passe
`USERNAME=root` et le Dockerfile saute l'alignement d'utilisateur (renommer le
compte root échouerait). En dev local (uid ≠ 0), rien ne change : utilisateur
non-root `devuser` aligné sur l'UID/GID de l'hôte, comme avant.

> **Limite connue (session web uniquement).** L'image se **construit** correctement,
> mais au **runtime** le conteneur ne peut pas exécuter les étapes backend :
> `dotnet restore` échoue (`NU1301 … RevocationStatusUnknown, OfflineRevocation`).
> L'egress transparent du conteneur présente un certificat sans point de révocation,
> or **NuGet exige une vérification de révocation TLS** ; il lui faudrait le proxy
> **explicite**, injoignable depuis un conteneur imbriqué (loopback de l'hôte ; le
> forwarding est bloqué par la politique de containment). `dotnet test`, `dotnet
> build` et `verify-e2e.sh` ne peuvent donc pas tourner dans le devcontainer d'une
> session web — les valider depuis un environnement de dev local. Sur une vraie
> machine (sans proxy intercepteur), aucune de ces limites ne s'applique.

## Utilisation normale : `scripts/feature-env.sh`

C'est le chemin prévu pour le travail parallèle — piloté depuis l'hôte (pas depuis un devcontainer, pour garder un accès Docker direct sans docker-outside-of-docker).

```bash
# Démarre le conteneur d'une worktree (chemin par défaut : .claude/worktrees/<name>)
bash scripts/feature-env.sh up billing-fix

# Récupère les URLs — toujours interrogé en direct, le port hôte peut changer
# à chaque redémarrage du conteneur (stop/start compris, pas seulement down/up)
bash scripts/feature-env.sh url billing-fix
#   Frontend: http://localhost:54217
#   API:      http://localhost:54218

# Lance une commande à l'intérieur du conteneur (ports internes toujours 3000/5203)
bash scripts/feature-env.sh exec billing-fix -- dotnet run --project src/HouseFlow.AppHost
bash scripts/feature-env.sh exec billing-fix -- bash scripts/verify-e2e.sh
bash scripts/feature-env.sh exec billing-fix -- dotnet test

# Arrête et nettoie (les données Postgres de CETTE feature persistent, sauf -v manuel)
bash scripts/feature-env.sh down billing-fix
```

Pour le checkout principal (pas une worktree), passe le chemin explicitement :
```bash
bash scripts/feature-env.sh up main .
```

Toujours invoquer via `bash scripts/feature-env.sh ...` plutôt que `./scripts/feature-env.sh ...` : le bit exécutable que git suit ne se transpose pas de façon fiable sur un checkout Windows.

Plusieurs features peuvent tourner simultanément — chacune a son propre réseau Docker, son propre Postgres, ses propres ports hôte. Rien à coordonner entre elles.

## Alternative : VS Code Dev Containers

Tu peux aussi ouvrir n'importe quelle worktree directement dans VS Code (`F1` → "Dev Containers: Reopen in Container"). `postCreateCommand` restaure automatiquement `dotnet restore` et `npm install`.

**Ne mélange pas les deux** sur la même worktree : VS Code calcule son propre nom de projet Docker Compose (différent de `houseflow-<name>`), donc ouvrir la même worktree à la fois via `feature-env.sh` et via VS Code donne deux stacks indépendantes avec deux bases Postgres différentes — source de confusion sur laquelle est à jour. Choisis un seul mode par worktree.

Ce mode sert au dev interactif classique (éditeur + terminal intégré dans le conteneur) — pas à faire tourner Claude Code, qui reste piloté depuis l'extérieur (voir plus haut).

## `dotnet test` dans le devcontainer

`dotnet test` tourne aussi à l'intérieur du conteneur, sans accès Docker. Le fixture de test (`IntegrationTestFixture`, dans `tests/HouseFlow.IntegrationTests/`) ne laisse plus Aspire spawner son propre Postgres éphémère — quand `POSTGRES_HOST` est présent, `Program.cs` connecte les tests à une base dédiée `houseflow_test` sur le même sidecar que le dev interactif (jamais la base `houseflow` elle-même, pour ne pas écraser tes données de dev). Cette base `houseflow_test` persiste entre les runs sur le sidecar (contrairement à un conteneur éphémère) — le fixture la `DROP`/recrée automatiquement au début de chaque run pour repartir d'un état propre à chaque fois.

Hors devcontainer (host, CI), `POSTGRES_HOST` n'est pas défini : Aspire spawne toujours son propre conteneur Postgres éphémère par run, comme avant — comportement inchangé.

## Limites connues

- **Le port hôte n'est pas stable** : il peut changer à chaque redémarrage du conteneur. Toujours ré-interroger via `feature-env.sh url`, ne jamais mémoriser un port d'une session précédente.
- **`houseflow_test` est remise à zéro une fois par run, pas par test** : comme avant (le fixture xUnit partage déjà une seule instance d'API/base entre tous les tests d'un run), donc les tests individuels doivent toujours gérer leurs propres données uniques — rien de neuf ici, juste rendu explicite.
- Le socket Docker n'est plus monté : si un jour l'app doit manipuler des conteneurs Docker depuis l'intérieur du devcontainer, il faudra ajouter la feature `docker-in-docker` (Docker imbriqué, pas socket partagé) plutôt que de remonter le socket de l'hôte.
- N features en parallèle = N conteneurs .NET/Node/Postgres simultanés. Pas de souci pour quelques features à la fois sur une machine correcte ; à surveiller si ça grimpe beaucoup plus haut.

## Sécurité et isolation

- Utilisateur non-root (`devuser`) aligné sur l'UID/GID de l'hôte — sauf si l'hôte est lui-même root (uid 0, cf. section proxy ci-dessus), auquel cas le conteneur reste root
- `--security-opt=no-new-privileges`
- Pas de socket Docker de l'hôte monté
- Volume limité au workspace (aucune config/credential Claude Code dans ce conteneur)

## Backend Rust

Un second backend, écrit en Rust (crate `rust/houseflow-api`, voir `rust/PORTING.md`),
cohabite avec le backend .NET pour la durée du portage. Le devcontainer installe le
toolchain Rust stable (rustup, sous `/usr/local/cargo` — voir `.devcontainer/Dockerfile`)
et publie son port dédié :

| Port | Service |
|------|---------|
| 5204 | API Rust (`houseflow-api`) |

`scripts/rust-api.sh` en pilote le cycle de vie, en miroir de `scripts/dev-api.sh` :

```bash
scripts/feature-env.sh exec <worktree> -- bash scripts/rust-api.sh build   # cargo build --release
scripts/feature-env.sh exec <worktree> -- bash scripts/rust-api.sh start  # démarre sur :5204 (base houseflow_rust)
scripts/feature-env.sh exec <worktree> -- bash scripts/rust-api.sh stop
scripts/feature-env.sh exec <worktree> -- bash scripts/rust-api.sh unit  # cargo test + clippy + fmt --check
```

Le backend Rust utilise deux bases dédiées sur le même sidecar Postgres — jamais
`houseflow`/`houseflow_test` (réservées au backend .NET) :
- `houseflow_rust` — dev interactif
- `houseflow_rust_test` — run black-box de la suite d'intégration .NET, recréée à
  chaque run par `scripts/rust-api.sh test`

Ce run black-box réutilise **la même suite** `tests/HouseFlow.IntegrationTests` que le
backend .NET, pointée sur le binaire Rust via `HOUSEFLOW_API_BASE_URL` :

```bash
scripts/feature-env.sh exec <worktree> -- bash scripts/rust-api.sh test
# équivaut à : HOUSEFLOW_API_BASE_URL=http://localhost:5214 dotnet test tests/HouseFlow.IntegrationTests
```

Un test qui passe contre les deux backends valide qu'ils exposent le même contrat.

## Dépannage

### Reconstruire le container d'une feature
```bash
scripts/feature-env.sh down billing-fix
scripts/feature-env.sh up billing-fix
```

### Valider le docker-compose avant de démarrer
```bash
docker compose -f .devcontainer/docker-compose.yml config
```
