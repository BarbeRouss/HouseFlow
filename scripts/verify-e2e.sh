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
if ! check_service "http://localhost:5203/swagger/index.html"; then
  echo "Backend not running. Starting..."
  bash "$PROJECT_DIR/scripts/dev-api.sh" start
  bash "$PROJECT_DIR/scripts/dev-api.sh" wait || { echo "ERROR: backend failed to start"; exit 1; }
else
  echo "Backend already running on :5203"
fi

# --- Frontend: build CSS + start Blazor dev server ---
echo "Building Tailwind CSS..."
( cd "$WEB_DIR" && [ -d node_modules ] || npm install --no-audit --no-fund >/dev/null 2>&1 )
( cd "$WEB_DIR" && npm run build:css >/dev/null 2>&1 ) || { echo "ERROR: CSS build failed"; exit 1; }

# A devserver started before a `dotnet build src/HouseFlow.Web` keeps serving the
# old index.html while the fingerprinted _framework assets on disk have been
# replaced: the WASM boot then fails (page stuck on the splash) and the whole
# suite times out. Detect that stale state and restart instead of running blind.
frontend_assets_ok() {
  local html asset code
  html=$(curl -s "http://localhost:3000/" 2>/dev/null) || return 1
  for asset in $(echo "$html" | grep -o '_framework/[A-Za-z0-9._-]*\.js' | sort -u); do
    code=$(curl -s -o /dev/null -w "%{http_code}" "http://localhost:3000/$asset" 2>/dev/null) || true
    [ "$code" = "200" ] || { echo "Stale frontend: $asset -> HTTP $code"; return 1; }
  done
}

if ! check_service "http://localhost:3000"; then
  echo "Frontend not running. Starting..."
  bash "$PROJECT_DIR/scripts/dev-web.sh" start
  bash "$PROJECT_DIR/scripts/dev-web.sh" wait || { echo "ERROR: frontend failed to start"; exit 1; }
elif ! frontend_assets_ok; then
  echo "Frontend running but serving stale assets. Restarting..."
  bash "$PROJECT_DIR/scripts/dev-web.sh" start
  bash "$PROJECT_DIR/scripts/dev-web.sh" wait || { echo "ERROR: frontend failed to restart"; exit 1; }
else
  echo "Frontend already running on :3000"
fi

# --- Playwright deps ---
( cd "$E2E_DIR" && [ -d node_modules ] || npm install --no-audit --no-fund >/dev/null 2>&1 )
if [ ! -d "$HOME/.cache/ms-playwright" ]; then
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
