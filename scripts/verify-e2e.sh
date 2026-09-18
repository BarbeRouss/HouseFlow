#!/bin/bash
set -uo pipefail

# HouseFlow - Verify E2E tests (Playwright) against the Blazor WebAssembly frontend.
# Starts the API (:5203) and the Blazor dev server (:3000) if needed, builds the
# Tailwind CSS, ensures Playwright + chromium are installed, runs the chromium
# suite, and writes a marker file on success for the pre-push hook.
#
# HOUSEFLOW_BACKEND=rust targets the Rust backend (:5204, scripts/rust-api.sh,
# see rust/PORTING.md) instead of the .NET one (:5203) — same E2E suite, same
# frontend, different backend underneath.
#
# Run this INSIDE the devcontainer:
#   scripts/feature-env.sh exec <worktree> -- bash scripts/verify-e2e.sh
#   HOUSEFLOW_BACKEND=rust scripts/feature-env.sh exec <worktree> -- bash scripts/verify-e2e.sh

PROJECT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
WEB_DIR="$PROJECT_DIR/src/HouseFlow.Web"
E2E_DIR="$PROJECT_DIR/e2e"
MARKER_FILE="/tmp/houseflow-e2e-verified"
PG_HOST="${POSTGRES_HOST:-postgres}"
BACKEND="${HOUSEFLOW_BACKEND:-dotnet}"
API_PORT=5203
API_SCRIPT="dev-api.sh"
if [ "$BACKEND" = "rust" ]; then
  API_PORT=5204
  API_SCRIPT="rust-api.sh"
fi

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
if ! check_service "http://localhost:$API_PORT/swagger/index.html"; then
  echo "Backend ($BACKEND) not running. Starting..."
  bash "$PROJECT_DIR/scripts/$API_SCRIPT" start
  bash "$PROJECT_DIR/scripts/$API_SCRIPT" wait || { echo "ERROR: backend failed to start"; exit 1; }
else
  echo "Backend ($BACKEND) already running on :$API_PORT"
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

# A frontend already running against the other backend has the wrong API_BASE_URL
# baked into wwwroot/appsettings.json (see HouseFlow.Web.csproj's WriteRuntimeConfig
# target) — dev-web.sh records which backend it was last started for.
frontend_backend_ok() {
  [ "$(cat /tmp/houseflow-web-backend 2>/dev/null)" = "$BACKEND" ]
}

if ! check_service "http://localhost:3000" || ! frontend_backend_ok; then
  echo "Frontend not running (or built for a different backend). Starting..."
  HOUSEFLOW_BACKEND="$BACKEND" bash "$PROJECT_DIR/scripts/dev-web.sh" start
  bash "$PROJECT_DIR/scripts/dev-web.sh" wait || { echo "ERROR: frontend failed to start"; exit 1; }
elif ! frontend_assets_ok; then
  echo "Frontend running but serving stale assets. Restarting..."
  HOUSEFLOW_BACKEND="$BACKEND" bash "$PROJECT_DIR/scripts/dev-web.sh" start
  bash "$PROJECT_DIR/scripts/dev-web.sh" wait || { echo "ERROR: frontend failed to restart"; exit 1; }
else
  echo "Frontend already running on :3000 ($BACKEND backend)"
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
