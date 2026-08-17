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

- Utilisateur non-root (`devuser`)
- `--security-opt=no-new-privileges`
- Pas de socket Docker de l'hôte monté
- Volume limité au workspace (aucune config/credential Claude Code dans ce conteneur)

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
