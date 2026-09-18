#!/bin/bash
# Manage the Blazor WASM frontend dev process on :3000 inside the devcontainer.
# Kept in a file so the pkill patterns don't accidentally match an interactive
# `feature-env exec` shell whose argv contains the same strings.
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ACTION="${1:-}"

# HOUSEFLOW_BACKEND=rust pointe le frontend sur le backend Rust (:5204, voir
# scripts/rust-api.sh et rust/PORTING.md) au lieu du backend .NET (:5203) — utile
# pour valider le frontend contre le port en cours de migration.
API_PORT=5203
[ "${HOUSEFLOW_BACKEND:-}" = "rust" ] && API_PORT=5204

stop() {
  pkill -9 -f "HouseFlow.WebHost.dll" 2>/dev/null || true
  pkill -9 -f "HouseFlow.Web.dll" 2>/dev/null || true
  pkill -9 -f "blazor-devserver" 2>/dev/null || true
  pkill -9 -f "dotnet-watch" 2>/dev/null || true
  sleep 2
}

case "$ACTION" in
  stop)
    stop
    ;;
  start)
    stop
    cd "$ROOT/src/HouseFlow.Web"
    # DEMO_MODE and API_BASE_URL are baked into wwwroot/appsettings.json by the
    # WriteRuntimeConfig MSBuild target (see HouseFlow.Web.csproj) so the login page
    # shows the one-click demo button and the app calls the right backend.
    # Marker so verify-e2e.sh can tell which backend an already-running devserver
    # was built against, and restart it if that no longer matches.
    echo "${HOUSEFLOW_BACKEND:-dotnet}" > /tmp/houseflow-web-backend
    setsid bash -c "DEMO_MODE='${DEMO_MODE:-true}' API_BASE_URL='http://localhost:$API_PORT' dotnet run -c Debug --urls http://0.0.0.0:3000 > /tmp/web.log 2>&1" < /dev/null &
    echo "started blazor dev server (log: /tmp/web.log)"
    ;;
  wait)
    for i in $(seq 1 60); do
      code=$(curl -s -o /dev/null -w "%{http_code}" "http://localhost:3000/_framework/dotnet.js" 2>/dev/null)
      [ "$code" = "200" ] && { echo "frontend ready"; exit 0; }
      sleep 2
    done
    echo "frontend did not become ready"; tail -20 /tmp/web.log; exit 1
    ;;
  *)
    echo "usage: dev-web.sh {start|stop|wait}" >&2; exit 1
    ;;
esac
