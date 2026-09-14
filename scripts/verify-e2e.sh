#!/bin/bash
set -uo pipefail

# HouseFlow - Verify E2E tests (Playwright) against the Blazor WebAssembly frontend.
# Starts the API (:5203) and the Blazor dev server (:3000) if needed, builds the
# Tailwind CSS, ensures Playwright + chromium are installed, runs the chromium
# suite, and writes a marker file on success for the pre-push hook.
#
# Run this INSIDE the devcontainer:
#   scripts/feature-env.sh exec <worktree> -- bash scripts/verify-e2e.sh

PROJECT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
WEB_DIR="$PROJECT_DIR/src/HouseFlow.Web"
E2E_DIR="$PROJECT_DIR/e2e"
MARKER_FILE="/tmp/houseflow-e2e-verified"
PG_HOST="${POSTGRES_HOST:-postgres}"
# Ports/DB are overridable so several worktrees can verify E2E side by side on one
# machine: API_PORT=5301 WEB_PORT=3301 DB_NAME=houseflow_x bash scripts/verify-e2e.sh
export API_PORT="${API_PORT:-5203}"
export WEB_PORT="${WEB_PORT:-3000}"
export DB_NAME="${DB_NAME:-houseflow}"
export FRONTEND_URL="http://localhost:$WEB_PORT"
# Some specs call the API directly (registering a user via HTTP before driving the UI).
# Without this they fall back to the default :5203 — i.e. another worktree's API and
# another database — and the user they create is invisible to the API under test.
export API_URL="http://localhost:$API_PORT"
# rbac-ui.spec.ts reads NEXT_PUBLIC_API_URL instead (leftover from the Next.js frontend).
export NEXT_PUBLIC_API_URL="$API_URL"

check_service() {
  local url=$1 code
  code=$(curl -s -o /dev/null -w "%{http_code}" "$url" 2>/dev/null) || true
  [[ "$code" =~ ^(200|301|302|307)$ ]]
}

# --- PostgreSQL ---
if ! PGPASSWORD=postgres psql -h "$PG_HOST" -U postgres -c "SELECT 1;" &>/dev/null; then
  echo "ERROR: PostgreSQL is not reachable at $PG_HOST."
  exit 1
fi

# --- Backend API ---
# Always (re)start: a server started before the last `dotnet build`/`dotnet test`
# runs a stale binary — the tests would validate old code.
echo "(Re)starting backend on :$API_PORT..."
bash "$PROJECT_DIR/scripts/dev-api.sh" start
bash "$PROJECT_DIR/scripts/dev-api.sh" wait || { echo "ERROR: backend failed to start"; exit 1; }

# --- Frontend: build CSS + start Blazor dev server ---
echo "Building Tailwind CSS..."
( cd "$WEB_DIR" && [ -d node_modules ] || npm install --no-audit --no-fund >/dev/null 2>&1 )
( cd "$WEB_DIR" && npm run build:css >/dev/null 2>&1 ) || { echo "ERROR: CSS build failed"; exit 1; }

# Always (re)start: after a `dotnet build src/HouseFlow.Web` the fingerprinted
# _framework assets change and a dev server started earlier serves a stale manifest
# (404 on dotnet.<hash>.js → the WASM app never boots, every test times out).
echo "(Re)starting frontend on :$WEB_PORT..."
bash "$PROJECT_DIR/scripts/dev-web.sh" start
bash "$PROJECT_DIR/scripts/dev-web.sh" wait || { echo "ERROR: frontend failed to start"; exit 1; }

# --- Playwright deps ---
( cd "$E2E_DIR" && [ -d node_modules ] || npm install --no-audit --no-fund >/dev/null 2>&1 )
if ! ls "${PLAYWRIGHT_BROWSERS_PATH:-$HOME/.cache/ms-playwright}"/chromium* >/dev/null 2>&1; then
  echo "Installing Playwright chromium..."
  ( cd "$E2E_DIR" && npx playwright install chromium >/dev/null 2>&1 )
  ( cd "$E2E_DIR" && sudo npx playwright install-deps chromium >/dev/null 2>&1 )
fi

# --- Run E2E (CI profile: 2 workers, retries, longer timeouts) ---
echo ""
echo "Running Playwright E2E tests (chromium)..."
cd "$E2E_DIR"
if CI=1 timeout -k 30s 12m npx playwright test --project=chromium; then
  date +%s > "$MARKER_FILE"
  echo ""
  echo "E2E tests PASSED. Marker written to $MARKER_FILE."
  exit 0
else
  rm -f "$MARKER_FILE"
  echo ""
  echo "E2E tests FAILED. Fix before pushing."
  exit 1
fi
