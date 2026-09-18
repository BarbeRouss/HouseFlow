#!/bin/bash
# Manage the Rust backend (crate rust/houseflow-api) dev process on :5204
# inside the devcontainer, en miroir de scripts/dev-api.sh pour le backend .NET.
# (pkill patterns live here, in a file, so they don't match the exec shell argv.)
set -uo pipefail

PROJECT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
RUST_DIR="$PROJECT_DIR/rust"
MANIFEST="$RUST_DIR/Cargo.toml"
BIN="$RUST_DIR/target/release/houseflow-api"
ACTION="${1:-}"
shift || true

PG_HOST="${POSTGRES_HOST:-postgres}"
PORT="${PORT:-5204}"
LOG_FILE="/tmp/rust-api.log"

# Clé JWT dev, alignée sur src/HouseFlow.API/appsettings.Development.json — même
# secret partagé entre les deux backends pour que les jetons émis par l'un soient
# acceptés par l'autre pendant la migration.
DEV_JWT_KEY='DevOnlySecretKey_DO_NOT_USE_IN_PRODUCTION_MinimumLengthRequired256Bits!'
DEV_JWT_ISSUER='HouseFlowAPI'
DEV_JWT_AUDIENCE='HouseFlowClient'

require_cargo() {
  if ! command -v cargo &>/dev/null; then
    echo "ERROR: cargo introuvable. Installe le toolchain Rust stable (rustup) :" >&2
    echo "  curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh -s -- -y" >&2
    echo "  source \"\$HOME/.cargo/env\"" >&2
    exit 1
  fi
}

require_crate() {
  if [ ! -f "$MANIFEST" ]; then
    echo "ERROR: crate Rust introuvable ($MANIFEST). Le backend Rust n'existe pas dans ce checkout." >&2
    exit 1
  fi
}

build() {
  require_cargo
  require_crate
  echo "Building houseflow-api (release)..."
  cargo build --release --manifest-path "$MANIFEST" -p houseflow-api
}

stop() {
  # Le nom du binaire ("houseflow-api") suffit à matcher le process qu'on a lancé
  # nous-même en detached, qu'il ait été démarré par start ou par test.
  pkill -9 -f "target/release/houseflow-api" 2>/dev/null || true
  sleep 2
}

wait_ready() {
  local port="$1" log="$2"
  for i in $(seq 1 30); do
    code=$(curl -s -o /dev/null -w "%{http_code}" "http://localhost:$port/swagger/index.html" 2>/dev/null)
    [[ "$code" =~ ^(200|301|302)$ ]] && { echo "rust api ready"; return 0; }
    sleep 2
  done
  echo "rust api did not become ready"; tail -30 "$log" 2>/dev/null; return 1
}

start() {
  require_crate
  stop
  if [ ! -x "$BIN" ]; then
    echo "ERROR: binaire absent ($BIN). Lance d'abord: $0 build" >&2
    exit 1
  fi
  local db_url="${DATABASE_URL:-postgres://postgres:postgres@$PG_HOST:5432/houseflow_rust}"
  setsid bash -c "DATABASE_URL='$db_url' PORT='$PORT' APP_ENV='Development' \
    AUTO_MIGRATE='true' DEMO_MODE='${DEMO_MODE:-true}' \
    JWT__KEY='$DEV_JWT_KEY' JWT__ISSUER='$DEV_JWT_ISSUER' JWT__AUDIENCE='$DEV_JWT_AUDIENCE' \
    ADMIN__BOOTSTRAP_EMAILS='julienrousselle@outlook.be,e2e-admin@houseflow.test' \
    CORS__ORIGINS='http://localhost:3000,http://127.0.0.1:3000' AUTH__COOKIE_SAME_SITE='None' \
    RUST_LOG='${RUST_LOG:-info}' \
    '$BIN' > '$LOG_FILE' 2>&1" < /dev/null &
  echo "started rust api on :$PORT (log: $LOG_FILE)"
}

drop_test_db() {
  local dbname="$1"
  # Même précaution que IntegrationTestFixture.ResetTestDatabaseAsync côté .NET :
  # on termine les sessions attachées avant le DROP (une instance précédente mal
  # arrêtée laisserait des connexions ouvertes qui feraient échouer le DROP).
  PGPASSWORD=postgres psql -h "$PG_HOST" -U postgres -d postgres -v dbname="$dbname" <<'SQL'
SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = :'dbname' AND pid <> pg_backend_pid();
DROP DATABASE IF EXISTS :"dbname";
SQL
}

run_blackbox_test() {
  require_cargo
  require_crate
  build || return 1

  local test_port="${TEST_PORT:-5214}"
  local test_db="houseflow_rust_test"
  local test_log="/tmp/rust-api-test.log"

  echo "Reset de la base $test_db..."
  # Stopper toute instance déjà en cours avant de toucher à la base — même logique
  # que le stop() en tête d'un start() classique.
  pkill -9 -f "target/release/houseflow-api" 2>/dev/null || true
  sleep 1
  drop_test_db "$test_db"

  local db_url="postgres://postgres:postgres@$PG_HOST:5432/$test_db"

  echo "Démarrage de houseflow-api sur :$test_port (base $test_db)..."
  setsid bash -c "DATABASE_URL='$db_url' PORT='$test_port' APP_ENV='Development' \
    AUTO_MIGRATE='true' DEMO_MODE='false' \
    JWT__KEY='$DEV_JWT_KEY' JWT__ISSUER='$DEV_JWT_ISSUER' JWT__AUDIENCE='$DEV_JWT_AUDIENCE' \
    ADMIN__BOOTSTRAP_EMAILS='julienrousselle@outlook.be' \
    AUTH__COOKIE_SAME_SITE='Lax' \
    RUST_LOG='${RUST_LOG:-info}' \
    '$BIN' > '$test_log' 2>&1" < /dev/null &

  local exit_code=0
  # trap : on arrête toujours le serveur de test, même si dotnet test échoue ou
  # que le script est interrompu.
  trap 'pkill -9 -f "target/release/houseflow-api" 2>/dev/null || true' EXIT

  if ! wait_ready "$test_port" "$test_log"; then
    echo "RESUME: échec démarrage houseflow-api (test black-box)"
    return 1
  fi

  echo "Exécution de la suite d'intégration .NET en mode black-box (HOUSEFLOW_API_BASE_URL=http://localhost:$test_port)..."
  HOUSEFLOW_API_BASE_URL="http://localhost:$test_port" \
    dotnet test "$PROJECT_DIR/tests/HouseFlow.IntegrationTests" "$@"
  exit_code=$?

  if [ $exit_code -eq 0 ]; then
    echo "RESUME: tests black-box PASSED contre le backend Rust (port $test_port)"
  else
    echo "RESUME: tests black-box FAILED contre le backend Rust (port $test_port, code $exit_code)"
  fi
  return $exit_code
}

run_unit() {
  require_cargo
  require_crate
  echo "cargo test..."
  cargo test --manifest-path "$MANIFEST" || return 1
  echo "cargo clippy..."
  cargo clippy --manifest-path "$MANIFEST" --all-targets -- -D warnings || return 1
  echo "cargo fmt --check..."
  cargo fmt --manifest-path "$MANIFEST" --check || return 1
  echo "RESUME: unit/clippy/fmt OK"
}

case "$ACTION" in
  build)
    build
    ;;
  start)
    start
    ;;
  stop)
    stop
    ;;
  wait)
    wait_ready "$PORT" "$LOG_FILE"
    ;;
  test)
    run_blackbox_test "$@"
    ;;
  unit)
    run_unit
    ;;
  *)
    echo "usage: rust-api.sh {build|start|stop|wait|test|unit}" >&2
    exit 1
    ;;
esac
