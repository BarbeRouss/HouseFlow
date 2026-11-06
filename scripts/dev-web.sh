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

# Host ports Docker published for :3000/:5203 (HOST_WEB_PORT / HOST_API_PORT), written
# into the container by `feature-env.sh up|url`. Only meaningful on the default ports:
# those are the ones published to the host.
HOST_PORTS_FILE="${HOST_PORTS_FILE:-/tmp/hf-host-ports.env}"
if [ "$WEB_PORT" = "3000" ] && [ "$API_PORT" = "5203" ] && [ -f "$HOST_PORTS_FILE" ]; then
  # shellcheck disable=SC1090
  . "$HOST_PORTS_FILE"
fi

stop() {
  # Only kill the frontend bound to OUR port (other worktrees may run their own).
  # "HouseFlow.WebHost.*--urls" matches both the `dotnet run --project …` wrapper and
  # the apphost it spawns; the next two catch a legacy blazor-devserver.
  pkill -9 -f "HouseFlow.WebHost.*--urls http://0.0.0.0:$WEB_PORT" 2>/dev/null || true
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
    cd "$ROOT"
    # Served through HouseFlow.WebHost (the host Aspire uses) rather than the WASM
    # devserver: its /appsettings.json endpoint resolves ApiBaseUrl PER REQUEST.
    # In-container clients (E2E on localhost:3000) get http://localhost:$API_PORT; a
    # browser on the host machine, coming in through the published port HOST_WEB_PORT,
    # gets http://localhost:$HOST_API_PORT. Both work at once, and survive restarts.
    # DEMO_MODE shows the one-click demo button on the login page.
    # Development environment = HouseFlow.Web's static web assets manifest (the fresh
    # _framework output of the build `dotnet run` just did) + Blazor-Environment header.
    # Redirections outside the `bash -c`, see dev-api.sh.
    setsid bash -c "ASPNETCORE_ENVIRONMENT=Development DEMO_MODE='${DEMO_MODE:-true}' API_BASE_URL='http://localhost:$API_PORT' \
      HOST_WEB_PORT='${HOST_WEB_PORT:-}' HOST_API_PORT='${HOST_API_PORT:-}' \
      dotnet run --project src/HouseFlow.WebHost -c Debug --no-launch-profile --urls http://0.0.0.0:$WEB_PORT" > /tmp/web-$WEB_PORT.log 2>&1 < /dev/null &
    echo "started frontend (HouseFlow.WebHost) on :$WEB_PORT → api :$API_PORT (log: /tmp/web-$WEB_PORT.log)"
    if [ -n "${HOST_WEB_PORT:-}" ] && [ -n "${HOST_API_PORT:-}" ]; then
      echo "  from the host: http://localhost:$HOST_WEB_PORT → api http://localhost:$HOST_API_PORT"
    fi
    ;;
  wait)
    # Ready = index.html served AND the (fingerprinted) boot script it references is
    # served. WebHost only serves fingerprinted _framework names (dotnet.<hash>.js…),
    # resolved through index.html's import map — probing a bare _framework/dotnet.js 404s.
    for i in $(seq 1 90); do
      asset=$(curl -s "http://localhost:$WEB_PORT/" 2>/dev/null | grep -o 'src="_framework/blazor\.webassembly[^"]*\.js"' | head -1 | cut -d'"' -f2)
      if [ -n "$asset" ]; then
        code=$(curl -s -o /dev/null -w "%{http_code}" "http://localhost:$WEB_PORT/$asset" 2>/dev/null)
        [ "$code" = "200" ] && { echo "frontend ready"; exit 0; }
      fi
      sleep 2
    done
    echo "frontend did not become ready"; tail -20 /tmp/web-$WEB_PORT.log; exit 1
    ;;
  *)
    echo "usage: dev-web.sh {start|stop|wait}" >&2; exit 1
    ;;
esac
