#!/usr/bin/env bash
# bench/run-backend.sh <dotnet|rust> <out_dir>
#
# Construit le backend en Release, repart d'une base dédiée vierge, démarre le
# serveur, mesure son démarrage, lance seed + scénarios, relève les métriques
# process, puis arrête le serveur. Tout atterrit dans <out_dir>.
#
# Réglages (env) :
#   POSTGRES_HOST        hôte Postgres                       (défaut localhost ; `postgres` dans le devcontainer)
#   BENCH_SKIP_BUILD=1   réutilise les artefacts existants
#   BENCH_COLD_BUILD=1   nettoie avant de construire (mesure un build à froid)
#   BENCH_PORT           port d'écoute                       (défaut 5223 .NET / 5224 Rust)
#   BENCH_DB             base de données                     (défaut houseflow_bench_<backend>)
#   BENCH_JWT_KEY        clé HS256 partagée par les deux backends
#   + tous les réglages de scenarios.sh (BENCH_DURATION, BENCH_CONCURRENCY, …)
set -euo pipefail

BACKEND=${1:-}
OUT_DIR=${2:-}

if [[ "$BACKEND" != "dotnet" && "$BACKEND" != "rust" ]] || [[ -z "$OUT_DIR" ]]; then
  echo "usage: run-backend.sh <dotnet|rust> <out_dir>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BENCH_DIR="${ROOT}/bench"
mkdir -p "$OUT_DIR"
OUT_DIR="$(cd "$OUT_DIR" && pwd)"

PG_HOST=${POSTGRES_HOST:-localhost}
PG_PORT=${POSTGRES_PORT:-5432}
PG_USER=${POSTGRES_USER:-postgres}
PG_PASSWORD=${POSTGRES_PASSWORD:-postgres}
JWT_KEY=${BENCH_JWT_KEY:-'DevOnlySecretKey_DO_NOT_USE_IN_PRODUCTION_MinimumLengthRequired256Bits!'}

if [[ "$BACKEND" == "dotnet" ]]; then
  PORT=${BENCH_PORT:-5223}
  DB=${BENCH_DB:-houseflow_bench_dotnet}
else
  PORT=${BENCH_PORT:-5224}
  DB=${BENCH_DB:-houseflow_bench_rust}
fi
BASE_URL="http://localhost:${PORT}"

PUBLISH_DIR="${BENCH_DIR}/.build/dotnet-publish"
RUST_BIN="${ROOT}/rust/target/release/houseflow-api"
SERVER_LOG="${OUT_DIR}/server.log"
SERVER_PID=""

command -v jq >/dev/null 2>&1 || { echo "run-backend: jq est requis" >&2; exit 2; }
command -v psql >/dev/null 2>&1 || { echo "run-backend: psql est requis" >&2; exit 2; }

if [[ "$BACKEND" == "dotnet" ]]; then
  # dotnet n'est pas toujours dans le PATH par défaut sur cette machine.
  if ! command -v dotnet >/dev/null 2>&1 && [[ -x /usr/share/dotnet/dotnet ]]; then
    export PATH="/usr/share/dotnet:${PATH}"
    export DOTNET_ROOT=/usr/share/dotnet
  fi
  command -v dotnet >/dev/null 2>&1 || { echo "run-backend: dotnet introuvable" >&2; exit 2; }
else
  if ! command -v cargo >/dev/null 2>&1 && [[ -x "${HOME}/.cargo/bin/cargo" ]]; then
    export PATH="${HOME}/.cargo/bin:${PATH}"
  fi
  command -v cargo >/dev/null 2>&1 || { echo "run-backend: cargo introuvable" >&2; exit 2; }
fi
if ! command -v oha >/dev/null 2>&1 && [[ -x "${HOME}/.cargo/bin/oha" ]]; then
  export PATH="${HOME}/.cargo/bin:${PATH}"
fi

stop_server() {
  if [[ -n "$SERVER_PID" ]] && kill -0 "$SERVER_PID" 2>/dev/null; then
    kill -TERM "$SERVER_PID" 2>/dev/null || true
    for _ in $(seq 1 50); do
      kill -0 "$SERVER_PID" 2>/dev/null || break
      sleep 0.2
    done
    if kill -0 "$SERVER_PID" 2>/dev/null; then
      echo "run-backend: SIGTERM ignoré, SIGKILL sur ${SERVER_PID}" >&2
      kill -KILL "$SERVER_PID" 2>/dev/null || true
    fi
  fi
  SERVER_PID=""
}
trap stop_server EXIT INT TERM

# --- Métriques /proc --------------------------------------------------------
CLK_TCK=$(getconf CLK_TCK 2>/dev/null || echo 100)
SAMPLES_FILE="${OUT_DIR}/.samples.jsonl"
: >"$SAMPLES_FILE"

sample_process() {
  local phase=$1
  local rss_kb=0 hwm_kb=0 cpu_s=0
  if [[ -n "$SERVER_PID" && -r "/proc/${SERVER_PID}/status" ]]; then
    rss_kb=$(awk '/^VmRSS:/ {print $2}' "/proc/${SERVER_PID}/status" 2>/dev/null || echo 0)
    hwm_kb=$(awk '/^VmHWM:/ {print $2}' "/proc/${SERVER_PID}/status" 2>/dev/null || echo 0)
    # champs 14 (utime) et 15 (stime) de /proc/<pid>/stat, après le `comm` entre parenthèses
    cpu_s=$(awk -v tck="$CLK_TCK" '{
      s = $0; sub(/^[^)]*\) /, "", s); split(s, f, " ");
      printf "%.3f", (f[12] + f[13]) / tck
    }' "/proc/${SERVER_PID}/stat" 2>/dev/null || echo 0)
  fi
  jq -nc --arg phase "$phase" --arg at "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
    --argjson rss "${rss_kb:-0}" --argjson hwm "${hwm_kb:-0}" --argjson cpu "${cpu_s:-0}" \
    '{phase:$phase, at:$at, rss_mb:(($rss/1024)*100|round/100), peak_rss_mb:(($hwm/1024)*100|round/100), cpu_seconds:$cpu}' \
    >>"$SAMPLES_FILE"
}

# --- 1. Build ---------------------------------------------------------------
BUILD_SECONDS=0
BUILD_SKIPPED=false
BUILD_COLD=${BENCH_COLD_BUILD:-0}
if [[ "${BENCH_SKIP_BUILD:-0}" == "1" ]]; then
  BUILD_SKIPPED=true
  echo "run-backend[${BACKEND}]: build ignoré (BENCH_SKIP_BUILD=1)"
else
  build_start=$(date +%s.%N)
  if [[ "$BACKEND" == "dotnet" ]]; then
    if [[ "$BUILD_COLD" == "1" ]]; then
      rm -rf "$PUBLISH_DIR"
      dotnet clean "${ROOT}/src/HouseFlow.API" -c Release >/dev/null 2>&1 || true
    fi
    echo "run-backend[dotnet]: dotnet publish -c Release ..."
    dotnet publish "${ROOT}/src/HouseFlow.API" -c Release -o "$PUBLISH_DIR" >"${OUT_DIR}/build.log" 2>&1 \
      || { echo "run-backend: build .NET en échec, voir ${OUT_DIR}/build.log" >&2; tail -30 "${OUT_DIR}/build.log" >&2; exit 1; }
  else
    if [[ "$BUILD_COLD" == "1" ]]; then
      cargo clean --release --manifest-path "${ROOT}/rust/Cargo.toml" >/dev/null 2>&1 || true
    fi
    echo "run-backend[rust]: cargo build --release ..."
    cargo build --release --manifest-path "${ROOT}/rust/Cargo.toml" -p houseflow-api >"${OUT_DIR}/build.log" 2>&1 \
      || { echo "run-backend: build Rust en échec, voir ${OUT_DIR}/build.log" >&2; tail -30 "${OUT_DIR}/build.log" >&2; exit 1; }
  fi
  build_end=$(date +%s.%N)
  BUILD_SECONDS=$(awk -v a="$build_start" -v b="$build_end" 'BEGIN{printf "%.2f", b-a}')
  echo "run-backend[${BACKEND}]: build en ${BUILD_SECONDS}s"
fi

if [[ "$BACKEND" == "dotnet" ]]; then
  ARTIFACT="${PUBLISH_DIR}/HouseFlow.API.dll"
  [[ -f "$ARTIFACT" ]] || { echo "run-backend: artefact absent: ${ARTIFACT} (lancer sans BENCH_SKIP_BUILD)" >&2; exit 1; }
  ARTIFACT_BYTES=$(stat -c %s "$ARTIFACT")
  DEPLOY_BYTES=$(du -sb "$PUBLISH_DIR" | cut -f1)
  RUNTIME_VERSION="dotnet $(dotnet --version)"
else
  ARTIFACT="$RUST_BIN"
  [[ -f "$ARTIFACT" ]] || { echo "run-backend: binaire absent: ${ARTIFACT} (lancer sans BENCH_SKIP_BUILD)" >&2; exit 1; }
  ARTIFACT_BYTES=$(stat -c %s "$ARTIFACT")
  DEPLOY_BYTES=$ARTIFACT_BYTES
  RUNTIME_VERSION="$(rustc --version 2>/dev/null || echo 'rustc ?')"
fi

# --- 2. Base de données vierge ---------------------------------------------
export PGPASSWORD="$PG_PASSWORD"
psql_admin() { psql -h "$PG_HOST" -p "$PG_PORT" -U "$PG_USER" -d postgres -v ON_ERROR_STOP=1 -q "$@"; }
echo "run-backend[${BACKEND}]: base ${DB} remise à zéro sur ${PG_HOST}:${PG_PORT}"
psql_admin -c "DROP DATABASE IF EXISTS \"${DB}\" WITH (FORCE);"
psql_admin -c "CREATE DATABASE \"${DB}\";"
PG_VERSION=$(psql -h "$PG_HOST" -p "$PG_PORT" -U "$PG_USER" -d postgres -tAc 'SHOW server_version;' 2>/dev/null | awk 'NR==1{print $1}')

# --- 3. Démarrage du serveur ------------------------------------------------
launch_start=$(date +%s.%N)
if [[ "$BACKEND" == "dotnet" ]]; then
  # --contentRoot : sans lui l'application cherche appsettings.json dans le
  # répertoire courant (et meurt sur « JWT Issuer not configured »).
  env -u ASPNETCORE_URLS \
    ConnectionStrings__houseflow="Host=${PG_HOST};Port=${PG_PORT};Database=${DB};Username=${PG_USER};Password=${PG_PASSWORD}" \
    ASPNETCORE_ENVIRONMENT=Development \
    DEMO_MODE=false \
    JWT__KEY="$JWT_KEY" \
    DOTNET_gcServer=1 \
    dotnet "$ARTIFACT" --urls "http://0.0.0.0:${PORT}" --contentRoot "$PUBLISH_DIR" >"$SERVER_LOG" 2>&1 &
  SERVER_PID=$!
else
  env DATABASE_URL="postgres://${PG_USER}:${PG_PASSWORD}@${PG_HOST}:${PG_PORT}/${DB}" \
    PORT="$PORT" \
    APP_ENV=Development \
    DEMO_MODE=false \
    AUTO_MIGRATE=true \
    JWT__KEY="$JWT_KEY" \
    "$ARTIFACT" >"$SERVER_LOG" 2>&1 &
  SERVER_PID=$!
fi
echo "run-backend[${BACKEND}]: pid=${SERVER_PID} port=${PORT} (log: ${SERVER_LOG})"

READY_TIMEOUT=${BENCH_READY_TIMEOUT:-180}
ready=false
give_up_at=$(( $(date +%s) + READY_TIMEOUT ))
while [[ $(date +%s) -lt $give_up_at ]]; do
  if ! kill -0 "$SERVER_PID" 2>/dev/null; then
    echo "run-backend: le serveur ${BACKEND} s'est arrêté avant d'être prêt" >&2
    tail -40 "$SERVER_LOG" >&2
    exit 1
  fi
  code=$(curl -sS -o /dev/null -w '%{http_code}' --max-time 2 "${BASE_URL}/swagger/index.html" 2>/dev/null || echo 000)
  if [[ "$code" == "200" ]]; then ready=true; break; fi
  sleep 0.05
done

launch_end=$(date +%s.%N)

if [[ "$ready" != true ]]; then
  echo "run-backend: ${BACKEND} n'est pas prêt après ${READY_TIMEOUT}s (sonde ${BASE_URL}/swagger/index.html)" >&2
  tail -40 "$SERVER_LOG" >&2
  exit 1
fi
STARTUP_MS=$(awk -v a="$launch_start" -v b="$launch_end" 'BEGIN{printf "%.0f", (b-a)*1000}')
echo "run-backend[${BACKEND}]: prêt en ${STARTUP_MS} ms"
sample_process before_seed

# --- 4. Seed ----------------------------------------------------------------
bash "${BENCH_DIR}/seed.sh" "$BASE_URL" "${OUT_DIR}/seed.json"
sample_process after_seed

# --- 5. Scénarios -----------------------------------------------------------
bash "${BENCH_DIR}/scenarios.sh" "$BASE_URL" "${OUT_DIR}/seed.json" "$OUT_DIR"
sample_process after_scenarios

# --- 6. process.json --------------------------------------------------------
jq -n \
  --arg backend "$BACKEND" \
  --arg runtime "$RUNTIME_VERSION" \
  --arg pgVersion "${PG_VERSION:-?}" \
  --arg database "$DB" \
  --arg baseUrl "$BASE_URL" \
  --arg artifact "${ARTIFACT#"${ROOT}/"}" \
  --argjson pid "$SERVER_PID" \
  --argjson port "$PORT" \
  --argjson startupMs "$STARTUP_MS" \
  --argjson buildSeconds "$BUILD_SECONDS" \
  --argjson buildSkipped "$BUILD_SKIPPED" \
  --arg buildCold "$BUILD_COLD" \
  --argjson artifactBytes "$ARTIFACT_BYTES" \
  --argjson deployBytes "$DEPLOY_BYTES" \
  --arg finishedAt "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
  --slurpfile samples "$SAMPLES_FILE" \
  '($samples | map({key: .phase, value: .}) | from_entries) as $byPhase
   | {
       backend: $backend,
       runtime: $runtime,
       postgres_version: $pgVersion,
       database: $database,
       base_url: $baseUrl,
       pid: $pid,
       port: $port,
       startup_ms: $startupMs,
       build: {seconds: $buildSeconds, skipped: $buildSkipped, cold: ($buildCold == "1"),
               artifact: $artifact, artifact_bytes: $artifactBytes, deploy_bytes: $deployBytes},
       samples: $samples,
       rss_mb_before_seed: ($byPhase.before_seed.rss_mb // null),
       rss_mb_after_seed: ($byPhase.after_seed.rss_mb // null),
       rss_mb_after_scenarios: ($byPhase.after_scenarios.rss_mb // null),
       peak_rss_mb: ($samples | map(.peak_rss_mb) | max),
       cpu_seconds_before_seed: ($byPhase.before_seed.cpu_seconds // null),
       cpu_seconds_after_scenarios: ($byPhase.after_scenarios.cpu_seconds // null),
       cpu_seconds_run: (($byPhase.after_scenarios.cpu_seconds // 0) - ($byPhase.before_seed.cpu_seconds // 0)),
       finished_at: $finishedAt
     }' >"${OUT_DIR}/process.json"
rm -f "$SAMPLES_FILE"

jq -r '"run-backend[\(.backend)]: démarrage \(.startup_ms)ms, RSS \(.rss_mb_before_seed)→\(.rss_mb_after_scenarios) Mo, CPU \(.cpu_seconds_run)s, artefact \((.build.artifact_bytes/1048576)*10|round/10) Mo"' \
  "${OUT_DIR}/process.json"

stop_server
echo "run-backend[${BACKEND}]: terminé -> ${OUT_DIR}"
