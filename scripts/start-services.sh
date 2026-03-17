#!/bin/bash
set -euo pipefail

# HouseFlow - Start all services
# Tries Aspire first, falls back to manual start if Aspire/DCP fails

PROJECT_DIR="$(cd "$(dirname "$0")/.." && pwd)"

cleanup() {
  echo "Stopping services..."
  kill $(jobs -p) 2>/dev/null || true
}
trap cleanup EXIT

# --- Try Aspire first ---
echo "Attempting to start via Aspire (AppHost)..."
ASPIRE_OK=false

# Start Aspire in background, give it 30s to boot
cd "$PROJECT_DIR"
dotnet run --project src/HouseFlow.AppHost &>/tmp/aspire.log &
ASPIRE_PID=$!

for i in $(seq 1 30); do
  # Check if Aspire process died
  if ! kill -0 $ASPIRE_PID 2>/dev/null; then
    echo "Aspire exited early. Falling back to manual start."
    break
  fi
  # Check if backend is reachable
  if curl -s -o /dev/null -w "%{http_code}" http://localhost:5203/swagger/index.html 2>/dev/null | grep -q "200"; then
    ASPIRE_OK=true
    echo "Aspire started successfully! Backend on :5203, Frontend on :3000"
    break
  fi
  sleep 2
done

if [ "$ASPIRE_OK" = true ]; then
  echo "Services running via Aspire. Press Ctrl+C to stop."
  wait $ASPIRE_PID
  exit 0
fi

# --- Fallback: manual start ---
echo "Starting services manually (PostgreSQL + .NET API + Next.js)..."

# Kill Aspire if still running
kill $ASPIRE_PID 2>/dev/null || true

# Ensure PostgreSQL is running
if ! PGPASSWORD=postgres psql -h localhost -U postgres -c "SELECT 1;" &>/dev/null 2>&1; then
  echo "ERROR: PostgreSQL is not running. Run init-session.sh first."
  exit 1
fi

# Start backend
echo "Starting backend on :5203..."
ConnectionStrings__houseflow="Host=localhost;Port=5432;Database=houseflow;Username=postgres;Password=postgres" \
  ASPNETCORE_URLS="http://localhost:5203" \
  ASPNETCORE_ENVIRONMENT="CI" \
  dotnet run --project "$PROJECT_DIR/src/HouseFlow.API" &>/tmp/backend.log &
BACKEND_PID=$!

# Wait for backend
for i in $(seq 1 30); do
  if curl -s -o /dev/null -w "%{http_code}" http://localhost:5203/swagger/index.html 2>/dev/null | grep -q "200"; then
    echo "Backend ready on :5203"
    break
  fi
  if [ $i -eq 30 ]; then
    echo "ERROR: Backend failed to start. Check /tmp/backend.log"
    exit 1
  fi
  sleep 2
done

# Start frontend
echo "Starting frontend on :3000..."
cd "$PROJECT_DIR/src/HouseFlow.Frontend"
npm run dev &>/tmp/frontend.log &
FRONTEND_PID=$!

for i in $(seq 1 15); do
  code=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:3000 2>/dev/null)
  if [ "$code" = "200" ] || [ "$code" = "307" ] || [ "$code" = "302" ]; then
    echo "Frontend ready on :3000"
    break
  fi
  sleep 2
done

echo ""
echo "All services running (manual mode):"
echo "  Backend:  http://localhost:5203 (Swagger: http://localhost:5203/swagger)"
echo "  Frontend: http://localhost:3000"
echo ""
echo "Press Ctrl+C to stop all services."
wait
