#!/bin/bash
# Run the HouseFlow app (API + Blazor frontend) in the devcontainer with FIXED
# host ports so it's reachable from the host — e.g. a Windows browser via WSL2
# localhost forwarding:
#
#   bash scripts/expose-app.sh [worktree-name]   # default: blazormig
#
#   Web : http://localhost:3000
#   API : http://localhost:5203  (Swagger: http://localhost:5203/swagger)
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
NAME="${1:-blazormig}"
COMPOSE="$ROOT/.devcontainer/docker-compose.yml"
OVERRIDE="$ROOT/.devcontainer/docker-compose.hostports.yml"
DC=(docker compose -p "houseflow-$NAME" -f "$COMPOSE" -f "$OVERRIDE")

echo "Bringing up the devcontainer with fixed host ports (web 3000, API 5203)..."
USER_UID="$(id -u)" USER_GID="$(id -g)" "${DC[@]}" up -d

echo "Building CSS and starting the API + frontend inside the container..."
"${DC[@]}" exec -T app bash -lc '
  set -e
  cd /workspace/src/HouseFlow.Web
  [ -d node_modules ] || npm install --no-audit --no-fund >/dev/null 2>&1
  npm run build:css >/dev/null 2>&1
  bash /workspace/scripts/dev-api.sh start
  bash /workspace/scripts/dev-api.sh wait
  bash /workspace/scripts/dev-web.sh start
  bash /workspace/scripts/dev-web.sh wait
'

echo ""
echo "Ready. From the Windows host, open:"
echo "  Web    : http://localhost:3000"
echo "  API    : http://localhost:5203/swagger"
