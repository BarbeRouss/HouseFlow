# DevContainer HouseFlow

Ce devcontainer sert deux usages : exécuter Claude Code dans un environnement isolé, et faire tourner l'app complète (API + Frontend + Postgres) pour du dev interactif sans polluer la machine locale. **Chaque worktree/feature obtient son propre projet Docker Compose** (son propre `app`, son propre `postgres`, son propre réseau) — pas de port ni de base à négocier entre sessions parallèles, chaque instance est simplement indépendante des autres.

## Architecture

Deux services docker-compose (`.devcontainer/docker-compose.yml`), le fichier est identique dans chaque worktree (c'est un checkout complet) :

- **`app`** : conteneur principal (SDK .NET 10 + workload Aspire, Node 20, Claude CLI). Publie les ports 3000 et 5203 vers l'hôte, mais **sans fixer le port hôte** — Docker choisit un port libre à chaque démarrage (voir `scripts/feature-env.sh url`).
- **`postgres`** : `postgres:17-alpine`, uniquement sur le réseau interne du projet (jamais publié sur l'hôte), joignable depuis `app` via l'hôte `postgres:5432`.

L'AppHost (`src/HouseFlow.AppHost/Program.cs`) détecte la présence de `POSTGRES_HOST` (injectée via `containerEnv`) et se connecte directement au conteneur `postgres` du même projet, plutôt que de demander à Aspire de spawner le sien via le socket Docker de l'hôte — ce dernier aurait été un conteneur frère non joignable simplement en `localhost` depuis le devcontainer. « Sidecar » ici désigne juste ce couple app+postgres au sein d'*un même* projet Compose — **pas** un serveur partagé entre plusieurs worktrees, ce modèle-là a été abandonné (voir plus haut). La base s'appelle toujours `houseflow` (pas de suffixe par worktree : chaque worktree a son propre serveur Postgres, donc rien à distinguer).

## Prérequis

- Docker Desktop installé et en cours d'exécution
- `jq` disponible sur la machine qui pilote `scripts/feature-env.sh` (pas dans le conteneur — sur l'hôte)
- Une clé API Anthropic (`ANTHROPIC_API_KEY`) si tu comptes lancer `claude` sans être déjà loggé

## Utilisation normale : `scripts/feature-env.sh`

C'est le chemin prévu pour le travail parallèle — piloté depuis l'hôte (pas depuis un devcontainer, pour garder un accès Docker direct sans docker-outside-of-docker).

```bash
# Démarre le conteneur d'une worktree (chemin par défaut : .claude/worktrees/<name>)
scripts/feature-env.sh up billing-fix

# Récupère les URLs — toujours interrogé en direct, le port hôte peut changer
# à chaque redémarrage du conteneur (stop/start compris, pas seulement down/up)
scripts/feature-env.sh url billing-fix
#   Frontend: http://localhost:54217
#   API:      http://localhost:54218

# Lance une commande à l'intérieur du conteneur (ports internes toujours 3000/5203)
scripts/feature-env.sh exec billing-fix -- dotnet run --project src/HouseFlow.AppHost
scripts/feature-env.sh exec billing-fix -- bash scripts/verify-e2e.sh

# Arrête et nettoie (les données Postgres de CETTE feature persistent, sauf -v manuel)
scripts/feature-env.sh down billing-fix
```

Pour le checkout principal (pas une worktree), passe le chemin explicitement :
```bash
scripts/feature-env.sh up main .
```

Plusieurs features peuvent tourner simultanément — chacune a son propre réseau Docker, son propre Postgres, ses propres ports hôte. Rien à coordonner entre elles.

## Alternative : VS Code Dev Containers

Tu peux aussi ouvrir n'importe quelle worktree directement dans VS Code (`F1` → "Dev Containers: Reopen in Container"). `postCreateCommand` restaure automatiquement `dotnet restore` et `npm install`.

**Ne mélange pas les deux** sur la même worktree : VS Code calcule son propre nom de projet Docker Compose (différent de `houseflow-<name>`), donc ouvrir la même worktree à la fois via `feature-env.sh` et via VS Code donne deux stacks indépendantes avec deux bases Postgres différentes — source de confusion sur laquelle est à jour. Choisis un seul mode par worktree.

### Lancer Claude dans le conteneur

```bash
claude --dangerously-skip-permissions
```

## Ce qui ne tourne PAS dans ce devcontainer

`dotnet test` (les tests d'intégration) reste à lancer sur la machine hôte ou en CI. Le fixture de test (`DistributedApplicationTestingBuilder`) fait spawner à Aspire son propre Postgres éphémère via Docker — ça nécessite un accès direct au démon Docker, qu'on a volontairement retiré du conteneur (pas de socket Docker monté). `verify-e2e.sh`, lui, fonctionne très bien à l'intérieur d'un conteneur de feature via `feature-env.sh exec ... -- bash scripts/verify-e2e.sh` (voir plus haut) — `POSTGRES_HOST` y vaut déjà `postgres` par défaut.

## Limites connues

- **Le port hôte n'est pas stable** : il peut changer à chaque redémarrage du conteneur. Toujours ré-interroger via `feature-env.sh url`, ne jamais mémoriser un port d'une session précédente.
- Le socket Docker n'est plus monté : si un jour tu as besoin que Claude ou l'app manipulent des conteneurs Docker depuis l'intérieur du devcontainer, il faudra ajouter la feature `docker-in-docker` (Docker imbriqué, pas socket partagé) plutôt que de remonter le socket de l'hôte.
- N features en parallèle = N conteneurs .NET/Node/Postgres simultanés. Pas de souci pour quelques features à la fois sur une machine correcte ; à surveiller si ça grimpe beaucoup plus haut.

## Sécurité et isolation

- Utilisateur non-root (`devuser`)
- `--security-opt=no-new-privileges`
- Pas de socket Docker de l'hôte monté
- Volumes limités au workspace + config Claude persistante entre rebuilds

## Dépannage

### Claude n'est pas trouvé
```bash
npm install -g @anthropic-ai/claude-code
```

### Reconstruire le container d'une feature
```bash
scripts/feature-env.sh down billing-fix
scripts/feature-env.sh up billing-fix
```

### Valider le docker-compose avant de démarrer
```bash
docker compose -f .devcontainer/docker-compose.yml config
```
