#!/usr/bin/env bash
# bench/scenarios.sh <base_url> <seed_json> <out_dir>
#
# Rejoue la liste figée des scénarios de charge contre <base_url>, avec le jeu de
# données produit par seed.sh, et écrit un JSON normalisé par scénario dans
# <out_dir>/scenario-<nom>.json.
#
# Réglages (env) :
#   BENCH_DURATION       durée de la mesure            (défaut 15s)
#   BENCH_CONCURRENCY    connexions simultanées        (défaut 64)
#   BENCH_LOGIN_CONCURRENCY  idem pour `login`         (défaut 8 : bcrypt cost 11, CPU bound)
#   BENCH_WARMUP         durée du run de chauffe jeté  (défaut 1s ; 0 = pas de chauffe)
#   BENCH_LOAD_TOOL      auto | oha | autocannon       (défaut auto : oha sinon npx autocannon)
#   BENCH_ONLY           liste de scénarios séparés par des virgules (défaut : tous)
#   BENCH_IGNORE_SMOKE   1 pour continuer même si le test à blanc d'un scénario n'est pas 2xx
set -euo pipefail

BASE_URL=${1:-}
SEED_JSON=${2:-}
OUT_DIR=${3:-}

if [[ -z "$BASE_URL" || -z "$SEED_JSON" || -z "$OUT_DIR" ]]; then
  echo "usage: scenarios.sh <base_url> <seed_json> <out_dir>" >&2
  exit 2
fi
command -v jq >/dev/null 2>&1 || { echo "scenarios: jq est requis" >&2; exit 2; }
[[ -f "$SEED_JSON" ]] || { echo "scenarios: seed introuvable: ${SEED_JSON}" >&2; exit 2; }

BASE_URL=${BASE_URL%/}
mkdir -p "$OUT_DIR"

DURATION=${BENCH_DURATION:-15s}
CONCURRENCY=${BENCH_CONCURRENCY:-64}
LOGIN_CONCURRENCY=${BENCH_LOGIN_CONCURRENCY:-8}
WARMUP=${BENCH_WARMUP:-1s}
ONLY=${BENCH_ONLY:-}

# --- Choix du générateur de charge -----------------------------------------
LOAD_TOOL=${BENCH_LOAD_TOOL:-auto}
if [[ "$LOAD_TOOL" == "auto" ]]; then
  if command -v oha >/dev/null 2>&1; then
    LOAD_TOOL=oha
  elif command -v npx >/dev/null 2>&1; then
    echo "scenarios: oha absent, repli sur autocannon via npx (installer oha : cargo install oha)" >&2
    LOAD_TOOL=autocannon
  else
    echo "scenarios: ni oha ni npx disponibles" >&2
    exit 2
  fi
fi

# oha >= 1.9 utilise `--output-format json` ; les versions antérieures `--json`.
OHA_JSON_FLAGS=()
if [[ "$LOAD_TOOL" == "oha" ]]; then
  if oha --help 2>&1 | grep -q -- '--output-format'; then
    OHA_JSON_FLAGS=(--output-format json)
  else
    OHA_JSON_FLAGS=(--json)
  fi
fi

echo "scenarios: générateur=${LOAD_TOOL} durée=${DURATION} concurrence=${CONCURRENCY} (login: ${LOGIN_CONCURRENCY})"

EMAIL=$(jq -r '.email' "$SEED_JSON")
PASSWORD=$(jq -r '.password' "$SEED_JSON")
HOUSE_ID=$(jq -r '.houseId' "$SEED_JSON")
DEVICE_ID=$(jq -r '.deviceId' "$SEED_JSON")
TYPE_ID=$(jq -r '.maintenanceTypeId' "$SEED_JSON")

LOGIN_BODY=$(jq -nc --arg e "$EMAIL" --arg p "$PASSWORD" '{email:$e, password:$p, rememberMe:false}')

# Le scénario `login` tourne sur les comptes dédiés du seed (un corps de requête
# par ligne) : marteler un seul compte mesurerait la contention sur ses propres
# sessions plutôt que le coût d'une authentification. Sans ces comptes (ou avec
# autocannon, qui ne sait pas alterner les corps), on retombe sur un seul compte.
LOGIN_BODIES_FILE="${OUT_DIR}/.login-bodies.txt"
jq -r '(.loginUsers // [])[] | {email:.email, password:.password, rememberMe:false} | tojson' \
  "$SEED_JSON" >"$LOGIN_BODIES_FILE"
LOGIN_USER_COUNT=$(wc -l <"$LOGIN_BODIES_FILE" | tr -d ' ')
if [[ "$LOAD_TOOL" != "oha" || "${LOGIN_USER_COUNT:-0}" -lt 2 ]]; then
  : >"$LOGIN_BODIES_FILE"
  LOGIN_USER_COUNT=1
fi
echo "scenarios: scénario login sur ${LOGIN_USER_COUNT} compte(s)"
INSTANCE_BODY=$(jq -nc --arg dt "$(date -u -d '30 days ago' +%Y-%m-%dT%H:%M:%SZ)" \
  '{date:$dt, cost:129.90, provider:"Bench Provider", notes:"Entretien de charge"}')
DEVICE_BODY=$(jq -nc '{name:"Appareil de charge", type:"Chaudiere Gaz", brand:"Viessmann", model:"Vitodens 200"}')

TOKEN=""

# Le JWT expire au bout de 15 min : on en reprend un frais avant chaque scénario
# plutôt que de porter celui du seed sur toute la campagne.
refresh_token() {
  local body
  body=$(curl -sS -X POST "${BASE_URL}/api/v1/auth/login" -H 'Content-Type: application/json' \
    --data-binary "$LOGIN_BODY") || { echo "scenarios: login impossible" >&2; exit 1; }
  TOKEN=$(jq -r '.accessToken // ""' <<<"$body")
  if [[ -z "$TOKEN" || "$TOKEN" == "null" ]]; then
    echo "scenarios: login sans accessToken: ${body}" >&2
    exit 1
  fi
}

# smoke <name> <method> <url> <body> <auth> : une requête à blanc, pour échouer
# vite et bruyamment si un payload est faux (plutôt que de mesurer des 400/s).
smoke() {
  local name=$1 method=$2 url=$3 body=$4 auth=$5
  local args=(-sS -o /dev/null -w '%{http_code}' -X "$method" "$url")
  if [[ "$auth" == "auth" ]]; then args+=(-H "Authorization: Bearer ${TOKEN}"); fi
  if [[ -n "$body" ]]; then args+=(-H 'Content-Type: application/json' --data-binary "$body"); fi
  local code
  code=$(curl "${args[@]}") || code="000"
  if [[ ! "$code" =~ ^2[0-9][0-9]$ ]]; then
    local detail_args=(-sS -X "$method" "$url")
    if [[ "$auth" == "auth" ]]; then detail_args+=(-H "Authorization: Bearer ${TOKEN}"); fi
    if [[ -n "$body" ]]; then detail_args+=(-H 'Content-Type: application/json' --data-binary "$body"); fi
    echo "scenarios: [${name}] ${method} ${url} -> HTTP ${code}" >&2
    curl "${detail_args[@]}" >&2 || true
    echo >&2
    if [[ "${BENCH_IGNORE_SMOKE:-0}" != "1" ]]; then exit 1; fi
  fi
}

# run_load <duration> <concurrency> <method> <url> <body> <auth> <raw_out> [bodies_file]
run_load() {
  local duration=$1 conc=$2 method=$3 url=$4 body=$5 auth=$6 raw_out=$7 bodies=${8:-}
  if [[ "$LOAD_TOOL" == "oha" ]]; then
    # -w : les requêtes en vol à l'échéance sont attendues au lieu d'être
    # comptées « aborted due to deadline » (sinon -c 64 produit 64 faux échecs).
    local args=(--no-tui "${OHA_JSON_FLAGS[@]}" -w -z "$duration" -c "$conc" -m "$method")
    if [[ "$auth" == "auth" ]]; then args+=(-H "Authorization: Bearer ${TOKEN}"); fi
    if [[ -n "$bodies" && -s "$bodies" ]]; then
      # -Z : un corps par ligne, les requêtes tournent sur le fichier.
      args+=(-T application/json -Z "$bodies")
    elif [[ -n "$body" ]]; then
      args+=(-T application/json -d "$body")
    fi
    args+=("$url")
    oha "${args[@]}" >"$raw_out"
  else
    # autocannon veut une durée en secondes (entier).
    local secs=${duration%s}
    local args=(--json -d "$secs" -c "$conc" -m "$method")
    if [[ "$auth" == "auth" ]]; then args+=(-H "Authorization: Bearer ${TOKEN}"); fi
    if [[ -n "$body" ]]; then args+=(-H 'Content-Type: application/json' -b "$body"); fi
    args+=("$url")
    npx --yes autocannon@7 "${args[@]}" >"$raw_out" 2>/dev/null
  fi
}

# normalise <name> <method> <path> <conc> <duration> <raw_json> <out_json>
normalise() {
  local name=$1 method=$2 path=$3 conc=$4 duration=$5 raw=$6 out=$7
  if [[ "$LOAD_TOOL" == "oha" ]]; then
    jq --arg name "$name" --arg method "$method" --arg path "$path" --arg tool oha \
      --argjson conc "$conc" --arg duration "$duration" '
      (.statusCodeDistribution // {}) as $codes
      | ([$codes | to_entries[] | select(.key | test("^2[0-9][0-9]$") | not) | .value] | add // 0) as $bad
      # « aborted due to deadline » = requête en vol à la fin du run, pas une erreur serveur.
      | ([(.errorDistribution // {}) | to_entries[]
          | select(.key | test("deadline") | not) | .value] | add // 0) as $errs
      | ([(.errorDistribution // {}) | to_entries[] | .value] | add // 0) as $allerrs
      | ([$codes | to_entries[] | .value] | add // 0) as $done
      | {
          name: $name, tool: $tool, method: $method, path: $path,
          concurrency: $conc, duration: $duration,
          requests_total: ($done + $allerrs),
          requests_per_sec: (.summary.requestsPerSec // 0),
          p50_ms: ((.latencyPercentiles.p50 // 0) * 1000),
          p95_ms: ((.latencyPercentiles.p95 // 0) * 1000),
          p99_ms: ((.latencyPercentiles.p99 // 0) * 1000),
          avg_ms: ((.summary.average // 0) * 1000),
          errors: ($bad + $errs),
          status_codes: $codes,
          error_distribution: (.errorDistribution // {}),
          raw: .
        }' "$raw" >"$out"
  else
    jq --arg name "$name" --arg method "$method" --arg path "$path" --arg tool autocannon \
      --argjson conc "$conc" --arg duration "$duration" '
      {
        name: $name, tool: $tool, method: $method, path: $path,
        concurrency: $conc, duration: $duration,
        requests_total: (.requests.total // 0),
        requests_per_sec: (.requests.average // 0),
        p50_ms: (.latency.p50 // 0),
        # autocannon ne publie pas p95 : p97.5 est la percentile la plus proche.
        p95_ms: (.latency.p97_5 // 0),
        p99_ms: (.latency.p99 // 0),
        avg_ms: (.latency.average // 0),
        errors: ((.errors // 0) + (.timeouts // 0) + (.non2xx // 0)),
        status_codes: {"2xx": (."2xx" // 0), "non2xx": (.non2xx // 0)},
        error_distribution: {},
        notes: ["p95_ms approximé par p97.5 (autocannon)"],
        raw: .
      }' "$raw" >"$out"
  fi
}

# --- Liste figée des scénarios ---------------------------------------------
# Champs séparés par US (0x1f) et non par une tabulation : `read` fusionne les
# délimiteurs blancs consécutifs, ce qui décalerait les champs des scénarios
# sans corps de requête.
SEP=$'\x1f'
# nom | méthode | chemin | corps | concurrence | auth|noauth | fichier de corps alternés
SCENARIOS=(
  "health${SEP}GET${SEP}/health${SEP}${SEP}${CONCURRENCY}${SEP}noauth${SEP}"
  "login${SEP}POST${SEP}/api/v1/auth/login${SEP}${LOGIN_BODY}${SEP}${LOGIN_CONCURRENCY}${SEP}noauth${SEP}${LOGIN_BODIES_FILE}"
  "houses_list${SEP}GET${SEP}/api/v1/houses${SEP}${SEP}${CONCURRENCY}${SEP}auth${SEP}"
  "house_detail${SEP}GET${SEP}/api/v1/houses/${HOUSE_ID}${SEP}${SEP}${CONCURRENCY}${SEP}auth${SEP}"
  "devices_list${SEP}GET${SEP}/api/v1/houses/${HOUSE_ID}/devices${SEP}${SEP}${CONCURRENCY}${SEP}auth${SEP}"
  "device_detail${SEP}GET${SEP}/api/v1/devices/${DEVICE_ID}${SEP}${SEP}${CONCURRENCY}${SEP}auth${SEP}"
  "upcoming_tasks${SEP}GET${SEP}/api/v1/upcoming-tasks${SEP}${SEP}${CONCURRENCY}${SEP}auth${SEP}"
  "maintenance_history${SEP}GET${SEP}/api/v1/devices/${DEVICE_ID}/maintenance-history${SEP}${SEP}${CONCURRENCY}${SEP}auth${SEP}"
  "create_instance${SEP}POST${SEP}/api/v1/maintenance-types/${TYPE_ID}/instances${SEP}${INSTANCE_BODY}${SEP}${CONCURRENCY}${SEP}auth${SEP}"
  "create_device${SEP}POST${SEP}/api/v1/houses/${HOUSE_ID}/devices${SEP}${DEVICE_BODY}${SEP}${CONCURRENCY}${SEP}auth${SEP}"
)

mkdir -p "${OUT_DIR}/raw"

for line in "${SCENARIOS[@]}"; do
  IFS="$SEP" read -r name method path body conc auth bodies <<<"$line"

  if [[ -n "$ONLY" && ",${ONLY}," != *",${name},"* ]]; then
    echo "scenarios: [${name}] ignoré (BENCH_ONLY)"
    continue
  fi

  url="${BASE_URL}${path}"
  refresh_token
  smoke "$name" "$method" "$url" "$body" "$auth"

  if [[ "$WARMUP" != "0" && "$WARMUP" != "0s" ]]; then
    run_load "$WARMUP" "$conc" "$method" "$url" "$body" "$auth" "${OUT_DIR}/raw/${name}.warmup.json" "$bodies"
  fi

  started=$(date -u +%Y-%m-%dT%H:%M:%SZ)
  run_load "$DURATION" "$conc" "$method" "$url" "$body" "$auth" "${OUT_DIR}/raw/${name}.json" "$bodies"
  finished=$(date -u +%Y-%m-%dT%H:%M:%SZ)

  normalise "$name" "$method" "$path" "$conc" "$DURATION" \
    "${OUT_DIR}/raw/${name}.json" "${OUT_DIR}/scenario-${name}.json"

  tmp=$(mktemp)
  jq --arg s "$started" --arg f "$finished" '. + {started_at:$s, finished_at:$f}' \
    "${OUT_DIR}/scenario-${name}.json" >"$tmp" && mv "$tmp" "${OUT_DIR}/scenario-${name}.json"

  jq -r '"scenarios: [\(.name)] \(.requests_per_sec | floor) req/s  p50=\(.p50_ms | .*100 | round / 100)ms  p95=\(.p95_ms | .*100 | round / 100)ms  erreurs=\(.errors)"' \
    "${OUT_DIR}/scenario-${name}.json"
done

echo "scenarios: terminé -> ${OUT_DIR}"
