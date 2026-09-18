#!/usr/bin/env bash
# bench/seed.sh <base_url> <out_json>
#
# Crée un jeu de données de benchmark réaliste via l'API publique (aucun accès SQL
# direct : ce qui marche pour .NET marche pour Rust), puis écrit dans <out_json>
# le JWT, les identifiants rejouables et les ids nécessaires aux scénarios.
#
# Volume par défaut (surchargeable par variables d'environnement) :
#   BENCH_SEED_HOUSES=3  BENCH_SEED_DEVICES=10  BENCH_SEED_TYPES=3  BENCH_SEED_INSTANCES=2
#   => 3 maisons, 30 appareils, 90 types d'entretien, 180 entretiens passés.
set -euo pipefail

BASE_URL=${1:-}
OUT_JSON=${2:-}

if [[ -z "$BASE_URL" || -z "$OUT_JSON" ]]; then
  echo "usage: seed.sh <base_url> <out_json>" >&2
  exit 2
fi
command -v jq >/dev/null 2>&1 || { echo "seed: jq est requis" >&2; exit 2; }
command -v curl >/dev/null 2>&1 || { echo "seed: curl est requis" >&2; exit 2; }

BASE_URL=${BASE_URL%/}

HOUSES=${BENCH_SEED_HOUSES:-3}
DEVICES_PER_HOUSE=${BENCH_SEED_DEVICES:-10}
TYPES_PER_DEVICE=${BENCH_SEED_TYPES:-3}
INSTANCES_PER_TYPE=${BENCH_SEED_INSTANCES:-2}

EMAIL="bench-$(date +%s)-${RANDOM}@houseflow.bench"
PASSWORD='BenchPass123!'
TOKEN=""
HTTP_CODE=""
BODY=""

# req <method> <path> [json_body] -> remplit HTTP_CODE et BODY
req() {
  local method=$1 path=$2 body=${3:-}
  local args=(-sS -X "$method" "${BASE_URL}${path}" -H 'Content-Type: application/json' -w $'\n%{http_code}')
  if [[ -n "$TOKEN" ]]; then args+=(-H "Authorization: Bearer ${TOKEN}"); fi
  if [[ -n "$body" ]]; then args+=(--data-binary "$body"); fi
  local out
  out=$(curl "${args[@]}") || { echo "seed: curl a échoué sur $method $path" >&2; exit 1; }
  HTTP_CODE=${out##*$'\n'}
  BODY=${out%$'\n'*}
}

expect_2xx() {
  local what=$1
  if [[ ! "$HTTP_CODE" =~ ^2[0-9][0-9]$ ]]; then
    echo "seed: ${what} a renvoyé HTTP ${HTTP_CODE}" >&2
    echo "seed: corps de la réponse: ${BODY}" >&2
    exit 1
  fi
}

# Une date passée, déterministe, en RFC 3339 (validation NotInFuture côté API).
days_ago() { date -u -d "${1} days ago" +%Y-%m-%dT%H:%M:%SZ; }

started=$(date +%s.%N)
echo "seed: cible ${BASE_URL}"

# --- 1. Compte de benchmark -------------------------------------------------
req POST /api/v1/auth/register "$(jq -nc --arg e "$EMAIL" --arg p "$PASSWORD" \
  '{email:$e, firstName:"Bench", lastName:"Mark", password:$p}')"
expect_2xx "register"
TOKEN=$(jq -r '.accessToken // ""' <<<"$BODY")
USER_ID=$(jq -r '.user.id // ""' <<<"$BODY")
if [[ -z "$TOKEN" || "$TOKEN" == "null" ]]; then
  echo "seed: pas d'accessToken dans la réponse de register: ${BODY}" >&2
  exit 1
fi

# Rejoue le login immédiatement : c'est exactement la requête du scénario `login`,
# on valide donc dès le seed que les identifiants rejouables fonctionnent.
req POST /api/v1/auth/login "$(jq -nc --arg e "$EMAIL" --arg p "$PASSWORD" \
  '{email:$e, password:$p, rememberMe:false}')"
expect_2xx "login"
TOKEN=$(jq -r '.accessToken // ""' <<<"$BODY")
if [[ -z "$USER_ID" || "$USER_ID" == "null" ]]; then USER_ID=$(jq -r '.user.id // ""' <<<"$BODY"); fi

# --- 1 bis. Comptes dédiés au scénario `login` ------------------------------
# Le scénario `login` fait tourner la charge sur plusieurs comptes distincts.
# Marteler un seul compte ne mesurerait pas bcrypt mais la contention sur ses
# propres sessions : `StartSessionAsync` purge les sessions au-delà de
# MaxSessionsPerUser, et deux connexions simultanées du même compte se disputent
# la suppression des mêmes lignes (500 côté .NET).
LOGIN_USERS=${BENCH_SEED_LOGIN_USERS:-16}
LOGIN_EMAILS=()
saved_token=$TOKEN
TOKEN=""
for ((u = 1; u <= LOGIN_USERS; u++)); do
  login_email="bench-login-${u}-$(date +%s)-${RANDOM}@houseflow.bench"
  req POST /api/v1/auth/register "$(jq -nc --arg e "$login_email" --arg p "$PASSWORD" --arg n "User${u}" \
    '{email:$e, firstName:"Bench", lastName:$n, password:$p}')"
  expect_2xx "register login user ${u}"
  LOGIN_EMAILS+=("$login_email")
done
TOKEN=$saved_token
echo "seed: ${#LOGIN_EMAILS[@]} comptes dédiés au scénario login"

# --- 2. Maisons -------------------------------------------------------------
HOUSE_IDS=()
for ((h = 1; h <= HOUSES; h++)); do
  req POST /api/v1/houses "$(jq -nc --arg n "Maison Bench ${h}" \
    '{name:$n, address:"12 rue du Benchmark", zipCode:"75001", city:"Paris"}')"
  expect_2xx "create house ${h}"
  HOUSE_IDS+=("$(jq -r '.id' <<<"$BODY")")
done
echo "seed: ${#HOUSE_IDS[@]} maisons créées"

# --- 3. Appareils / types d'entretien / entretiens --------------------------
PERIODICITIES=(Annual Semestrial Quarterly Monthly)
FIRST_DEVICE_ID=""
FIRST_TYPE_ID=""
device_count=0
type_count=0
instance_count=0

for house_id in "${HOUSE_IDS[@]}"; do
  for ((d = 1; d <= DEVICES_PER_HOUSE; d++)); do
    req POST "/api/v1/houses/${house_id}/devices" \
      "$(jq -nc --arg n "Appareil ${d}" --arg i "$(days_ago $((365 * 3)))" \
        '{name:$n, type:"Chaudiere Gaz", brand:"Viessmann", model:"Vitodens 200", installDate:$i}')"
    expect_2xx "create device ${d}"
    device_id=$(jq -r '.id' <<<"$BODY")
    device_count=$((device_count + 1))
    if [[ -z "$FIRST_DEVICE_ID" ]]; then FIRST_DEVICE_ID=$device_id; fi

    for ((t = 1; t <= TYPES_PER_DEVICE; t++)); do
      periodicity=${PERIODICITIES[$(((t - 1) % ${#PERIODICITIES[@]}))]}
      req POST "/api/v1/devices/${device_id}/maintenance-types" \
        "$(jq -nc --arg n "Entretien ${t}" --arg p "$periodicity" '{name:$n, periodicity:$p}')"
      expect_2xx "create maintenance type ${t}"
      type_id=$(jq -r '.id' <<<"$BODY")
      type_count=$((type_count + 1))
      if [[ -z "$FIRST_TYPE_ID" ]]; then FIRST_TYPE_ID=$type_id; fi

      for ((i = 1; i <= INSTANCES_PER_TYPE; i++)); do
        # Dates passées étalées : scores, statuts et prochaines échéances sont
        # ainsi non triviaux (mélange up_to_date / pending / overdue).
        offset=$((40 + (i - 1) * 300 + (t - 1) * 25))
        req POST "/api/v1/maintenance-types/${type_id}/instances" \
          "$(jq -nc --arg dt "$(days_ago "$offset")" --argjson c "$((80 + offset % 120)).50" \
            '{date:$dt, cost:$c, provider:"Chauffagiste Pro", notes:"Entretien de seed"}')"
        expect_2xx "log maintenance ${i}"
        instance_count=$((instance_count + 1))
      done
    done
  done
  echo "seed: maison ${house_id} remplie (${device_count} appareils au total)"
done

finished=$(date +%s.%N)
elapsed=$(awk -v a="$started" -v b="$finished" 'BEGIN{printf "%.2f", b-a}')

mkdir -p "$(dirname "$OUT_JSON")"
jq -n \
  --arg baseUrl "$BASE_URL" \
  --arg email "$EMAIL" \
  --arg password "$PASSWORD" \
  --arg token "$TOKEN" \
  --arg userId "$USER_ID" \
  --arg houseId "${HOUSE_IDS[0]}" \
  --arg deviceId "$FIRST_DEVICE_ID" \
  --arg maintenanceTypeId "$FIRST_TYPE_ID" \
  --argjson houseIds "$(printf '%s\n' "${HOUSE_IDS[@]}" | jq -R . | jq -s .)" \
  --argjson loginEmails "$(printf '%s\n' "${LOGIN_EMAILS[@]}" | jq -R . | jq -s .)" \
  --argjson devices "$device_count" \
  --argjson types "$type_count" \
  --argjson instances "$instance_count" \
  --arg seededAt "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
  --argjson seedSeconds "$elapsed" \
  '{baseUrl:$baseUrl, email:$email, password:$password, token:$token, userId:$userId,
    houseId:$houseId, houseIds:$houseIds, deviceId:$deviceId, maintenanceTypeId:$maintenanceTypeId,
    loginUsers: ([$loginEmails[] | {email:., password:$password}]),
    counts:{houses:($houseIds|length), devices:$devices, maintenanceTypes:$types,
            maintenanceInstances:$instances, loginUsers:($loginEmails|length)},
    seededAt:$seededAt, seedSeconds:$seedSeconds}' >"$OUT_JSON"

echo "seed: ${device_count} appareils, ${type_count} types, ${instance_count} entretiens en ${elapsed}s -> ${OUT_JSON}"
