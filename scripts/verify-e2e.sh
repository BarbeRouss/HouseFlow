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

# A devserver started before a `dotnet build src/HouseFlow.Web` keeps serving
# stale output: the old index.html references fingerprinted _framework assets
# that no longer exist (404), and the precompressed variants (.gz/.br, which is
# what a browser actually receives) are regenerated under its feet and come
# back empty. Either way the WASM boot fails and the whole suite times out.
# Probe like a browser (compressed) and restart instead of running blind.
frontend_assets_ok() {
  local html asset code body
  html=$(curl -s "$FRONTEND_URL/" 2>/dev/null) || return 1
  for asset in $(echo "$html" | grep -o '_framework/[A-Za-z0-9._-]*\.js' | sort -u); do
    code=$(curl -s -o /dev/null -w "%{http_code}" -H "Accept-Encoding: gzip, deflate, br" "$FRONTEND_URL/$asset" 2>/dev/null) || true
    [ "$code" = "200" ] || { echo "Stale frontend: $asset -> HTTP $code"; return 1; }
  done
  # The runtime config is rewritten by every build (WriteRuntimeConfig target):
  # it must decode to JSON and carry the demo mode the suite relies on.
  body=$(curl -s --compressed -H "Accept-Encoding: gzip, deflate, br" "$FRONTEND_URL/appsettings.json" 2>/dev/null) || true
  echo "$body" | grep -q '"DemoMode": *"true"' || { echo "Stale frontend: appsettings.json -> '${body:-<empty>}'"; return 1; }
}

if ! check_service "$FRONTEND_URL"; then
  echo "Frontend not running. Starting on :$WEB_PORT..."
  bash "$PROJECT_DIR/scripts/dev-web.sh" start
  bash "$PROJECT_DIR/scripts/dev-web.sh" wait || { echo "ERROR: frontend failed to start"; exit 1; }
elif ! frontend_assets_ok; then
  echo "Frontend running but serving stale assets. Restarting on :$WEB_PORT..."
  bash "$PROJECT_DIR/scripts/dev-web.sh" start
  bash "$PROJECT_DIR/scripts/dev-web.sh" wait || { echo "ERROR: frontend failed to restart"; exit 1; }
else
  echo "Frontend already running on :$WEB_PORT"
fi

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
