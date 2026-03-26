#!/bin/bash
set -euo pipefail

# HouseFlow - Verify E2E tests (Playwright)
# Starts services if not running, runs Playwright chromium suite,
# and writes a marker file on success for the pre-push hook.

PROJECT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
FRONTEND_DIR="$PROJECT_DIR/src/HouseFlow.Frontend"
MARKER_FILE="/tmp/houseflow-e2e-verified"

check_service() {
  local url=$1
  local code
  code=$(curl -s -o /dev/null -w "%{http_code}" "$url" 2>/dev/null) || true
  [[ "$code" =~ ^(200|302|307)$ ]]
}

# --- Ensure backend is running ---
if ! check_service "http://localhost:5203/swagger/index.html"; then
  echo "Backend not running. Starting..."

  # Ensure PostgreSQL is available
  if ! PGPASSWORD=postgres psql -h localhost -U postgres -c "SELECT 1;" &>/dev/null 2>&1; then
    echo "ERROR: PostgreSQL is not running. Run scripts/init-session.sh first."
    exit 1
  fi

  ConnectionStrings__houseflow="Host=localhost;Port=5432;Database=houseflow;Username=postgres;Password=postgres" \
    ASPNETCORE_URLS="http://localhost:5203" \
    ASPNETCORE_ENVIRONMENT="CI" \
    dotnet run --project "$PROJECT_DIR/src/HouseFlow.API" &>/tmp/backend.log &

  for i in $(seq 1 30); do
    if check_service "http://localhost:5203/swagger/index.html"; then
      echo "Backend ready on :5203"
      break
    fi
    if [ "$i" -eq 30 ]; then
      echo "ERROR: Backend failed to start. Check /tmp/backend.log"
      exit 1
    fi
    sleep 2
  done
else
  echo "Backend already running on :5203"
fi

# --- Ensure frontend is running ---
if ! check_service "http://localhost:3000"; then
  echo "Frontend not running. Starting..."
  cd "$FRONTEND_DIR" && npm run dev &>/tmp/frontend.log &

  for i in $(seq 1 15); do
    if check_service "http://localhost:3000"; then
      echo "Frontend ready on :3000"
      break
    fi
    if [ "$i" -eq 15 ]; then
      echo "ERROR: Frontend failed to start. Check /tmp/frontend.log"
      exit 1
    fi
    sleep 2
  done
else
  echo "Frontend already running on :3000"
fi

# --- Run E2E tests ---
echo ""
echo "Running Playwright E2E tests (chromium)..."
cd "$FRONTEND_DIR"

if npx playwright test --project=chromium; then
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
