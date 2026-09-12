#!/bin/bash
# Manage the HouseFlow API dev process on :5203 inside the devcontainer.
# (pkill patterns live here, in a file, so they don't match the exec shell argv.)
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ACTION="${1:-}"
PG_HOST="${POSTGRES_HOST:-postgres}"
# Overridable so several worktrees can run E2E side by side on one machine
# (each with its own API port and its own database on the same Postgres).
API_PORT="${API_PORT:-5203}"
DB_NAME="${DB_NAME:-houseflow}"
WEB_PORT="${WEB_PORT:-3000}"

stop() {
  # Match the `dotnet run` wrapper, the built HouseFlow.API.dll, AND the native
  # apphost exe (…/bin/Debug/net10.0/HouseFlow.API) — the last one has no ".dll"
  # in its argv, so a narrower pattern leaves it holding port 5203.
  # Only kill the API bound to OUR port (other worktrees may run their own on another port).
  pkill -9 -f "HouseFlow.API.*--urls 'http://0.0.0.0:$API_PORT'" 2>/dev/null || true
  pkill -9 -f "HouseFlow.API.*--urls http://0.0.0.0:$API_PORT" 2>/dev/null || true
  # Legacy match (no --urls in argv, e.g. apphost exe) only when we own the default port.
  if [ "$API_PORT" = "5203" ]; then pkill -9 -f "bin/Debug/net10.0/HouseFlow.API$" 2>/dev/null || true; fi
  sleep 2
}

case "$ACTION" in
  stop)
    stop
    ;;
  start)
    stop
    cd "$ROOT"
    # --urls (passed to the app) beats the launch profile's applicationUrl, so the
    # API binds 0.0.0.0 and is reachable through Docker's published port, not just
    # from inside the container.
    setsid bash -c "ConnectionStrings__houseflow='Host=$PG_HOST;Port=5432;Database=$DB_NAME;Username=postgres;Password=postgres' \
      ASPNETCORE_ENVIRONMENT='CI' DEMO_MODE='${DEMO_MODE:-true}' \
      Admin__BootstrapEmails__1='e2e-admin@houseflow.test' \
      CORS__ORIGINS='http://localhost:$WEB_PORT,http://localhost:3000' \
      dotnet run --project src/HouseFlow.API -c Debug --urls 'http://0.0.0.0:$API_PORT' > /tmp/api-$API_PORT.log 2>&1" < /dev/null &
    echo "started api on :$API_PORT, db $DB_NAME (log: /tmp/api-$API_PORT.log)"
    ;;
  wait)
    for i in $(seq 1 90); do
      code=$(curl -s -o /dev/null -w "%{http_code}" "http://localhost:$API_PORT/swagger/index.html" 2>/dev/null)
      [[ "$code" =~ ^(200|301|302)$ ]] && { echo "api ready"; exit 0; }
      sleep 2
    done
    echo "api did not become ready"; tail -30 /tmp/api-$API_PORT.log; exit 1
    ;;
  *)
    echo "usage: dev-api.sh {start|stop|wait}" >&2; exit 1
    ;;
esac
