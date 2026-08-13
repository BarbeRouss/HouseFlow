# DevContainer HouseFlow

Ce devcontainer sert deux usages : exécuter Claude Code dans un environnement isolé, et faire tourner l'app complète (API + Frontend + Postgres) pour du dev interactif sans polluer la machine locale ni entrer en conflit de ports avec d'autres sessions.

## Architecture

Deux services docker-compose (`.devcontainer/docker-compose.yml`) :

- **`app`** : conteneur principal (SDK .NET 10 + workload Aspire, Node 20, Claude CLI). C'est celui auquel VS Code s'attache.
- **`postgres`** : `postgres:17-alpine`, uniquement sur le réseau interne du compose (pas exposé sur l'hôte), joignable depuis `app` via l'hôte `postgres:5432`.

L'AppHost (`src/HouseFlow.AppHost/Program.cs`) détecte la présence de `POSTGRES_HOST` (injectée via `containerEnv`) et se connecte directement au sidecar `postgres`, plutôt que de demander à Aspire de spawner son propre conteneur Postgres via le socket Docker de l'hôte — ce dernier aurait été un conteneur frère non joignable simplement en `localhost` depuis le devcontainer.

### Une base par worktree

Quand `POSTGRES_HOST` est présent, l'AppHost détecte automatiquement s'il tourne depuis une worktree (chemin contenant `.claude/worktrees/<nom>`) et se connecte à une base dédiée `houseflow_<nom>` sur le même serveur Postgres, plutôt qu'à la base `houseflow` partagée. Comme `dbContext.Database.Migrate()` tourne au démarrage de l'API, cette base est créée et migrée automatiquement au premier lancement — aucune étape manuelle. Résultat : une modification de schéma testée dans une worktree ne touche jamais la base des autres worktrees ni celle de la session principale.

Pour forcer un nom explicitement (au lieu de la détection automatique par chemin), définis `WORKTREE_NAME` avant de lancer l'AppHost.

## Prérequis

- Docker Desktop installé et en cours d'exécution
- Visual Studio Code avec l'extension "Dev Containers"
- Une clé API Anthropic (`ANTHROPIC_API_KEY`) si tu comptes lancer `claude` sans être déjà loggé

## Utilisation

1. Ouvre le projet dans VS Code
2. `F1` → "Dev Containers: Reopen in Container"
3. Premier build : télécharge/installe le SDK .NET, le workload Aspire, Node, Claude CLI — peut prendre plusieurs minutes
4. `postCreateCommand` restaure automatiquement les dépendances .NET (`dotnet restore`) et npm (`npm install` dans `src/HouseFlow.Frontend`)

### Lancer l'app

```bash
dotnet run --project src/HouseFlow.AppHost
```

- Frontend : http://localhost:3000 (forwardé automatiquement par VS Code, notification à l'ouverture)
- API : http://localhost:5203 (Swagger sur `/swagger`)
- Dashboard Aspire : port dynamique, affiché dans la console au démarrage

### Ports personnalisés (validation manuelle / instances parallèles)

Les ports par défaut (5203/3000) restent inchangés si tu ne définis rien. Pour lancer une deuxième instance en parallèle (ex: depuis une worktree pendant qu'une autre session tourne déjà) :

```bash
API_PORT=5213 FRONTEND_PORT=3010 dotnet run --project src/HouseFlow.AppHost
```

### Lancer Claude

```bash
claude --dangerously-skip-permissions
```

## Ce qui ne tourne PAS dans ce devcontainer

`dotnet test` (les tests d'intégration) reste à lancer sur la machine hôte ou en CI. Le fixture de test (`DistributedApplicationTestingBuilder`) fait spawner à Aspire son propre Postgres éphémère via Docker — ça nécessite un accès direct au démon Docker, qu'on a volontairement retiré du conteneur (plus de socket Docker monté) pour simplifier l'isolation. `bash scripts/verify-e2e.sh` (E2E Playwright), lui, fonctionne très bien dans le devcontainer :

```bash
POSTGRES_HOST=postgres bash scripts/verify-e2e.sh
```

(`POSTGRES_HOST` vaut déjà `postgres` par défaut dans ce conteneur via `containerEnv` — la commande ci-dessus est explicite mais pas obligatoire.)

## Limites connues

- **Une seule instance Postgres partagée** pour toutes les worktrees (une base *par worktree*, mais un seul serveur/volume). Un `docker compose down -v` sur le sidecar efface donc les bases de toutes les worktrees en même temps, pas juste une.
- Le socket Docker n'est plus monté : si un jour tu as besoin que Claude ou l'app manipulent des conteneurs Docker depuis l'intérieur du devcontainer, il faudra ajouter la feature `docker-in-docker` (Docker imbriqué, pas socket partagé) plutôt que de remonter le socket de l'hôte.

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

### Reconstruire le container
`F1` → "Dev Containers: Rebuild Container", ou supprime le container et relance.

### Valider le docker-compose avant d'ouvrir VS Code
```bash
docker compose -f .devcontainer/docker-compose.yml config
```
