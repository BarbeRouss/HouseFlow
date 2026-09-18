#!/bin/bash
set -euo pipefail

# HouseFlow - Per-feature devcontainer wrapper
# Each feature/worktree gets its own docker-compose project (app + postgres),
# isolated network, own ports (Docker picks a free host port for each).
# Usage:
#   scripts/feature-env.sh up <name> [path]      # path defaults to .claude/worktrees/<name>
#   scripts/feature-env.sh down <name>
#   scripts/feature-env.sh url <name>
#   scripts/feature-env.sh exec <name> -- <cmd...>

PROJECT_DIR="$(cd "$(dirname "$0")/.." && pwd)"

usage() {
  echo "Usage: $0 {up|down|url|exec} <name> [args...]" >&2
  exit 1
}

require_jq() {
  command -v jq &>/dev/null || {
    echo "ERROR: jq is required (used to parse 'docker compose ps --format json')." >&2
    exit 1
  }
}

compose_file_for() {
  local name=$1 path=$2
  echo "${path:-$PROJECT_DIR/.claude/worktrees/$name}/.devcontainer/docker-compose.yml"
}

cmd_up() {
  local name=$1 path=${2:-}
  local compose_file
  compose_file="$(compose_file_for "$name" "$path")"

  if [ ! -f "$compose_file" ]; then
    echo "ERROR: no compose file at $compose_file" >&2
    echo "Pass an explicit path, e.g.: $0 up main $PROJECT_DIR" >&2
    exit 1
  fi

  # Rendre la CA du proxy d'egress (Claude Code web / proxy d'entreprise) disponible
  # au build de l'image pour que curl/apt/dotnet vérifient le TLS re-terminé. Le
  # Dockerfile la COPY toujours, donc on garantit un placeholder VIDE en local — le
  # build reste alors strictement identique à aujourd'hui (voir .devcontainer/Dockerfile).
  local ctx_dir ca_dst proxy_ca
  ctx_dir="$(cd "$(dirname "$compose_file")" && pwd)"
  ca_dst="$ctx_dir/proxy-ca.crt"
  proxy_ca="${CCR_CA_BUNDLE:-/root/.ccr/ca-bundle.crt}"
  if [ -f "$proxy_ca" ]; then
    cp "$proxy_ca" "$ca_dst"
    echo "Proxy CA détectée → injectée dans le build ($proxy_ca)"
  else
    : > "$ca_dst"
  fi

  # Align the container's non-root user with the host user's UID/GID so files
  # written inside the bind-mounted /workspace (dotnet build/test, npm install,
  # dotnet new, ...) are writable — the bind mount enforces host Unix permissions,
  # so a mismatched UID silently loses write access to the whole repo.
  # Quand l'hôte tourne en root (uid 0, ex. Claude Code web), le conteneur doit
  # rester root pour écrire dans le bind mount /workspace possédé par root ; le
  # Dockerfile saute alors la création d'utilisateur (voir .devcontainer/Dockerfile).
  local container_user="devuser"
  [ "$(id -u)" = "0" ] && container_user="root"
  USER_UID="$(id -u)" USER_GID="$(id -g)" USERNAME="$container_user" \
    docker compose -p "houseflow-$name" -f "$compose_file" up -d --build
  cmd_url "$name"
}

cmd_down() {
  local name=$1
  docker compose -p "houseflow-$name" down
}

cmd_url() {
  local name=$1
  require_jq

  # Always re-queried live: the host port Docker assigns is only stable for the
  # lifetime of the container, and changes on every recreate (down+up, rebuild, ...).
  local ps_json
  ps_json="$(docker compose -p "houseflow-$name" ps --format json app 2>/dev/null)"
  if [ -z "$ps_json" ]; then
    echo "ERROR: no running 'app' container for project houseflow-$name (did you run 'up'?)" >&2
    exit 1
  fi

  # docker compose ps --format json has printed either one JSON object per line or a
  # single JSON array depending on version — normalize both to an array.
  local publishers
  publishers="$(echo "$ps_json" | jq -s 'map(if type == "array" then . else [.] end) | add | (.[0].Publishers // [])')"

  local frontend_port api_port rust_api_port
  frontend_port="$(echo "$publishers" | jq -r '.[] | select(.TargetPort == 3000) | .PublishedPort' | head -1)"
  api_port="$(echo "$publishers" | jq -r '.[] | select(.TargetPort == 5203) | .PublishedPort' | head -1)"
  rust_api_port="$(echo "$publishers" | jq -r '.[] | select(.TargetPort == 5204) | .PublishedPort' | head -1)"

  echo "Frontend:  http://localhost:${frontend_port:-?}"
  echo "API:       http://localhost:${api_port:-?}"
  echo "API Rust:  http://localhost:${rust_api_port:-?}"
}

cmd_exec() {
  local name=$1
  shift
  if [ "${1:-}" = "--" ]; then
    shift
  fi
  docker compose -p "houseflow-$name" exec app "$@"
}

[ $# -ge 2 ] || usage
action=$1
name=$2
shift 2

case "$action" in
  up)   cmd_up "$name" "${1:-}" ;;
  down) cmd_down "$name" ;;
  url)  cmd_url "$name" ;;
  exec) cmd_exec "$name" "$@" ;;
  *) usage ;;
esac
