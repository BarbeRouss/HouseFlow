#!/bin/bash
# Manage the HouseFlow API dev process on :5203 inside the devcontainer.
# (pkill patterns live here, in a file, so they don't match the exec shell argv.)
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ACTION="${1:-}"
PG_HOST="${POSTGRES_HOST:-postgres}"

stop() {
  # Match the `dotnet run` wrapper, the built HouseFlow.API.dll, AND the native
  # apphost exe (…/bin/Debug/net10.0/HouseFlow.API) — the last one has no ".dll"
  # in its argv, so a narrower pattern leaves it holding port 5203.
  pkill -9 -f "HouseFlow.API" 2>/dev/null || true
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
    setsid bash -c "ConnectionStrings__houseflow='Host=$PG_HOST;Port=5432;Database=houseflow;Username=postgres;Password=postgres' \
      ASPNETCORE_ENVIRONMENT='CI' DEMO_MODE='${DEMO_MODE:-true}' \
      dotnet run --project src/HouseFlow.API -c Debug --urls 'http://0.0.0.0:5203' > /tmp/api.log 2>&1" < /dev/null &
    echo "started api (log: /tmp/api.log)"
    ;;
  wait)
    for i in $(seq 1 90); do
      code=$(curl -s -o /dev/null -w "%{http_code}" "http://localhost:5203/swagger/index.html" 2>/dev/null)
      [[ "$code" =~ ^(200|301|302)$ ]] && { echo "api ready"; exit 0; }
      sleep 2
    done
    echo "api did not become ready"; tail -30 /tmp/api.log; exit 1
    ;;
  *)
    echo "usage: dev-api.sh {start|stop|wait}" >&2; exit 1
    ;;
esac
