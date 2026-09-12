#!/bin/bash
# Run / stop the Playwright E2E suite inside the devcontainer. Kept in a file so
# the pkill patterns don't match an interactive exec shell's own argv.
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
E2E_DIR="$ROOT/e2e"
ACTION="${1:-}"
shift || true

stop() {
  # Scope the kill to THIS worktree's Playwright (argv contains its e2e/ path):
  # several worktrees may run E2E side by side on one machine.
  pkill -9 -f "$E2E_DIR/node_modules/.bin/playwright" 2>/dev/null || true
  pkill -9 -f "$E2E_DIR/node_modules/@playwright" 2>/dev/null || true
  pkill -9 -f "$E2E_DIR/node_modules/playwright" 2>/dev/null || true
  sleep 1
}

case "$ACTION" in
  stop)
    stop
    ;;
  run)
    stop
    cd "$E2E_DIR"
    FRONTEND_URL="${FRONTEND_URL:-http://localhost:${WEB_PORT:-3000}}" \
    API_URL="${API_URL:-http://localhost:${API_PORT:-5203}}" \
    NEXT_PUBLIC_API_URL="${NEXT_PUBLIC_API_URL:-${API_URL:-http://localhost:${API_PORT:-5203}}}" \
    CI=1 npx playwright test --project=chromium --reporter=line "$@"
    ;;
  *)
    echo "usage: e2e.sh {stop|run [spec...]}" >&2; exit 1
    ;;
esac
