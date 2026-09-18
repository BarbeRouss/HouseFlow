#!/usr/bin/env bash
# bench/run-comparison.sh [--only dotnet|rust] [--skip-build] [--out-dir <dir>]
#
# Campagne complète : .NET, puis Rust, puis génération de docs/perf/rust-vs-dotnet.md.
# Échoue bruyamment (code retour non nul) si un backend ne démarre pas, et ne laisse
# jamais un serveur derrière elle.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BENCH_DIR="${ROOT}/bench"

ONLY=""
SKIP_BUILD=0
OUT_DIR="${BENCH_DIR}/results"
REPORT_OUT="docs/perf/rust-vs-dotnet.md"

usage() {
  cat <<'USAGE'
usage: run-comparison.sh [options]

  --only <dotnet|rust>   ne mesurer qu'un seul backend (l'autre reste en n/a dans le rapport)
  --skip-build           réutiliser les artefacts déjà construits
  --out-dir <dir>        où écrire les mesures (défaut: bench/results)
  --report <fichier>     rapport Markdown à produire (défaut: docs/perf/rust-vs-dotnet.md)
  -h, --help             cette aide

Réglages par variables d'environnement : BENCH_DURATION, BENCH_CONCURRENCY,
BENCH_LOGIN_CONCURRENCY, BENCH_WARMUP, BENCH_LOAD_TOOL, POSTGRES_HOST… (voir bench/README.md)
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --only)
      ONLY=${2:-}
      [[ "$ONLY" == "dotnet" || "$ONLY" == "rust" ]] || { echo "--only attend dotnet ou rust" >&2; exit 2; }
      shift 2
      ;;
    --skip-build) SKIP_BUILD=1; shift ;;
    --out-dir) OUT_DIR=${2:-}; [[ -n "$OUT_DIR" ]] || { usage >&2; exit 2; }; shift 2 ;;
    --report) REPORT_OUT=${2:-}; [[ -n "$REPORT_OUT" ]] || { usage >&2; exit 2; }; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "option inconnue: $1" >&2; usage >&2; exit 2 ;;
  esac
done

mkdir -p "$OUT_DIR"
OUT_DIR="$(cd "$OUT_DIR" && pwd)"

CHILD_PID=""
cleanup() {
  if [[ -n "$CHILD_PID" ]] && kill -0 "$CHILD_PID" 2>/dev/null; then
    echo "run-comparison: interruption, arrêt de la campagne en cours (pid ${CHILD_PID})" >&2
    # run-backend.sh a son propre trap : un SIGTERM lui fait arrêter son serveur.
    kill -TERM "$CHILD_PID" 2>/dev/null || true
    wait "$CHILD_PID" 2>/dev/null || true
  fi
  CHILD_PID=""
}
trap cleanup EXIT INT TERM

# assert_port_free <backend> <port> : filet de sécurité, on ne part jamais en
# laissant un serveur de bench en écoute.
assert_port_free() {
  local backend=$1 port=$2 code
  code=$(curl -sS -o /dev/null -w '%{http_code}' --max-time 2 "http://localhost:${port}/swagger/index.html" 2>/dev/null || echo 000)
  if [[ "$code" != "000" ]]; then
    echo "run-comparison: ATTENTION, quelque chose écoute encore sur le port ${port} après le run ${backend}" >&2
    return 1
  fi
  return 0
}

run_backend() {
  local backend=$1 port=$2
  echo "=== run-comparison: backend ${backend} ==="
  BENCH_SKIP_BUILD="$SKIP_BUILD" bash "${BENCH_DIR}/run-backend.sh" "$backend" "${OUT_DIR}/${backend}" &
  CHILD_PID=$!
  local status=0
  wait "$CHILD_PID" || status=$?
  CHILD_PID=""
  if [[ $status -ne 0 ]]; then
    echo "run-comparison: la campagne ${backend} a échoué (code ${status})" >&2
    assert_port_free "$backend" "$port" || true
    exit "$status"
  fi
  assert_port_free "$backend" "$port" || true
}

DOTNET_DIR=""
RUST_DIR=""

if [[ -z "$ONLY" || "$ONLY" == "dotnet" ]]; then
  run_backend dotnet "${BENCH_DOTNET_PORT:-5223}"
  DOTNET_DIR="${OUT_DIR}/dotnet"
fi

if [[ -z "$ONLY" || "$ONLY" == "rust" ]]; then
  if [[ ! -f "${ROOT}/rust/Cargo.toml" ]]; then
    echo "run-comparison: le backend Rust n'existe pas encore (${ROOT}/rust/Cargo.toml absent)" >&2
    if [[ "$ONLY" == "rust" ]]; then exit 1; fi
  else
    run_backend rust "${BENCH_RUST_PORT:-5224}"
    RUST_DIR="${OUT_DIR}/rust"
  fi
fi

# Un backend déjà mesuré lors d'une campagne précédente reste pris en compte,
# pour que `--only rust` ne fasse pas disparaître la colonne .NET du rapport.
if [[ -z "$DOTNET_DIR" && -d "${OUT_DIR}/dotnet" ]]; then DOTNET_DIR="${OUT_DIR}/dotnet"; fi
if [[ -z "$RUST_DIR" && -d "${OUT_DIR}/rust" ]]; then RUST_DIR="${OUT_DIR}/rust"; fi

echo "=== run-comparison: rapport ==="
REPORT_ARGS=(--out "$REPORT_OUT")
if [[ -n "$DOTNET_DIR" ]]; then REPORT_ARGS+=(--dotnet "$DOTNET_DIR"); fi
if [[ -n "$RUST_DIR" ]]; then REPORT_ARGS+=(--rust "$RUST_DIR"); fi
python3 "${BENCH_DIR}/report.py" "${REPORT_ARGS[@]}"

echo "run-comparison: terminé. Mesures dans ${OUT_DIR}, rapport dans ${REPORT_OUT}."
