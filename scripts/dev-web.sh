#!/bin/bash
# Manage the Blazor WASM frontend dev process on :3000 inside the devcontainer.
# Kept in a file so the pkill patterns don't accidentally match an interactive
# `feature-env exec` shell whose argv contains the same strings.
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ACTION="${1:-}"
# Overridable so several worktrees can run E2E side by side (see dev-api.sh).
WEB_PORT="${WEB_PORT:-3000}"
API_PORT="${API_PORT:-5203}"

stop() {
  # Only kill the dev server bound to OUR port (other worktrees may run their own).
  pkill -9 -f "blazor-devserver.*--urls http://0.0.0.0:$WEB_PORT" 2>/dev/null || true
  pkill -9 -f "HouseFlow.Web.dll.*--urls http://0.0.0.0:$WEB_PORT" 2>/dev/null || true
  if [ "$WEB_PORT" = "3000" ]; then
    pkill -9 -f "HouseFlow.WebHost.dll" 2>/dev/null || true
    pkill -9 -f "dotnet-watch" 2>/dev/null || true
  fi
  sleep 2
}

case "$ACTION" in
  stop)
    stop
    ;;
  start)
    stop
    cd "$ROOT/src/HouseFlow.Web"
    # DEMO_MODE is baked into wwwroot/appsettings.json by the WriteRuntimeConfig
    # MSBuild target so the login page shows the one-click demo button.
    setsid bash -c "DEMO_MODE='${DEMO_MODE:-true}' API_BASE_URL='http://localhost:$API_PORT' dotnet run -c Debug --urls http://0.0.0.0:$WEB_PORT > /tmp/web-$WEB_PORT.log 2>&1" < /dev/null &
    echo "started blazor dev server on :$WEB_PORT → api :$API_PORT (log: /tmp/web-$WEB_PORT.log)"
    ;;
  wait)
    for i in $(seq 1 60); do
      code=$(curl -s -o /dev/null -w "%{http_code}" "http://localhost:$WEB_PORT/_framework/dotnet.js" 2>/dev/null)
      [ "$code" = "200" ] && { echo "frontend ready"; exit 0; }
      sleep 2
    done
    echo "frontend did not become ready"; tail -20 /tmp/web-$WEB_PORT.log; exit 1
    ;;
  *)
    echo "usage: dev-web.sh {start|stop|wait}" >&2; exit 1
    ;;
esac
