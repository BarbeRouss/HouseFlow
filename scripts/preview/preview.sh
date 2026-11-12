#!/usr/bin/env bash
#
# preview.sh — previews de PR sur le homelab : un projet docker compose `pr-<n>` par PR
# (deploy/preview/compose.yaml, issue #351). Fait pour tourner sur la LXC `houseflow-preview`.
#
#   preview.sh up [--no-pull] <n> [<dump>]   tire les images, (re)déploie, restaure le dump, attend l'API
#   preview.sh down <n>                      supprime le projet : conteneurs, volume, images, état
#   preview.sh ls                            les previews et la date de leur dernier déploiement
#   preview.sh reap <ttl_heures>             `down` des previews plus vieilles que le TTL, purge des images
#
# Aucun `docker build` ici : les images `ghcr.io/barberouss/houseflow-preview-{api,web,dbtools}:pr-<n>`
# sont construites ailleurs et seulement tirées. `--no-pull` garde celles déjà présentes
# localement (essai manuel, repli) — voir deploy/preview/README.md.
#
# État : `$PREVIEW_ROOT/pr-<n>/` (défaut `$HOME/previews`)
#   .env          secrets du projet (mot de passe PostgreSQL, clé JWT), 0600, générés au premier `up`
#   dump/         dump à restaurer, supprimé après chaque `up` (et par dbtools après la restauration)
#   last-deploy   date (UTC) du dernier déploiement réussi
#
# Réglages — variable d'environnement, ou ligne `CLE=valeur` dans `$PREVIEW_ROOT/preview.conf` :
#   PREVIEW_HOMELAB_PROXY   adresse (ou réseau CIDR) du Traefik du homelab, obligatoire pour `up` :
#                           c'est le second proxy dont l'API croit X-Forwarded-For
#   PREVIEW_NETWORK         réseau Docker externe lu par le Traefik de la LXC (défaut : previews)
#   PREVIEW_DOMAIN          zone des previews (défaut : pr.houseflow.cloud)
#   IMAGE_TAG               tag des images (défaut : pr-<n>)
#
# Les secrets ne sont jamais affichés ni journalisés par ce script.

set -euo pipefail
umask 077

readonly IMAGE_REPO="ghcr.io/barberouss/houseflow-preview"
readonly IMAGE_NAMES=(api web dbtools)
readonly WAIT_TIMEOUT_SECONDS=300

SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
readonly SCRIPT_DIR
readonly COMPOSE_FILE_PATH="${PREVIEW_COMPOSE_FILE:-$SCRIPT_DIR/../../deploy/preview/compose.yaml}"
readonly PREVIEW_ROOT="${PREVIEW_ROOT:-$HOME/previews}"

log() { printf '[preview] %s\n' "$*" >&2; }
die() {
  printf '[preview] ERREUR : %s\n' "$*" >&2
  exit 1
}

# Valeur d'un réglage : environnement, puis preview.conf, puis défaut. Lu par grep et non
# `source` : un fichier de réglages ne doit pas pouvoir exécuter quoi que ce soit.
setting() {
  local key="$1" default="${2:-}" value="${!1:-}"
  if [ -z "$value" ] && [ -f "$PREVIEW_ROOT/preview.conf" ]; then
    value=$(grep -E "^${key}=" "$PREVIEW_ROOT/preview.conf" | tail -n 1 | cut -d= -f2- || true)
  fi
  printf '%s' "${value:-$default}"
}

require_tools() {
  local tool
  for tool in docker flock od; do
    command -v "$tool" >/dev/null 2>&1 || die "commande requise introuvable : $tool"
  done
}

validate_pr() {
  [[ "${1:-}" =~ ^[1-9][0-9]*$ ]] || die "numéro de PR invalide : « ${1:-} » (entier positif attendu)"
}

state_dir() { printf '%s/pr-%s' "$PREVIEW_ROOT" "$1"; }

# Un seul `up` / `down` / `reap` à la fois sur la machine : un reap ne doit pas détruire une
# preview en cours de déploiement.
acquire_lock() {
  mkdir -p "$PREVIEW_ROOT"
  exec 9>"$PREVIEW_ROOT/.lock"
  flock -w 900 9 || die "verrou $PREVIEW_ROOT/.lock indisponible après 15 minutes"
}

# Le projet compose `pr-<n>`. Les variables du fichier compose sont posées ici ; les secrets
# viennent du .env du projet (--env-file), jamais de la ligne de commande. Sans .env (projet déjà
# supprimé, `down` idempotent), des valeurs factices suffisent à interpoler le fichier.
compose() {
  local n="$1"
  shift
  local dir secrets=() env_args=()
  dir=$(state_dir "$n")
  if [ -f "$dir/.env" ]; then
    env_args=(--env-file "$dir/.env")
  else
    secrets=(POSTGRES_PASSWORD=unused JWT_KEY=unused)
  fi
  env \
    "${secrets[@]+"${secrets[@]}"}" \
    PR_NUMBER="$n" \
    IMAGE_TAG="${IMAGE_TAG:-pr-$n}" \
    PREVIEW_NETWORK="$(setting PREVIEW_NETWORK previews)" \
    PREVIEW_DOMAIN="$(setting PREVIEW_DOMAIN pr.houseflow.cloud)" \
    PREVIEW_DUMP_DIR="$dir/dump" \
    PREVIEW_PROXY_NETWORKS="${PREVIEW_PROXY_NETWORKS:-unused}" \
    docker compose -p "pr-$n" -f "$COMPOSE_FILE_PATH" "${env_args[@]+"${env_args[@]}"}" "$@"
}

generate_secret() { od -An -tx1 -N32 /dev/urandom | tr -d ' \n'; }

# Générés une fois : un `up` suivant garde la même base (son mot de passe est celui du volume)
# et les mêmes sessions.
ensure_secrets() {
  local env_file="$1/.env" tmp
  [ ! -f "$env_file" ] || return 0
  log "Génération des secrets du projet."
  tmp=$(mktemp "$1/.env.XXXXXX")
  {
    printf 'POSTGRES_PASSWORD=%s\n' "$(generate_secret)"
    printf 'JWT_KEY=%s\n' "$(generate_secret)"
  } >"$tmp"
  chmod 0600 "$tmp"
  mv "$tmp" "$env_file"
}

# Réseaux dont l'API croit X-Forwarded-* : le sous-réseau du réseau Docker externe (d'où
# arrive le Traefik de la LXC) et le Traefik du homelab (l'entrée avant lui dans la chaîne).
proxy_networks() {
  local network="$1" homelab subnets
  homelab=$(setting PREVIEW_HOMELAB_PROXY)
  [ -n "$homelab" ] ||
    die "PREVIEW_HOMELAB_PROXY manquant : adresse du Traefik du homelab, nécessaire pour que l'API connaisse l'IP réelle du client (variable d'environnement ou $PREVIEW_ROOT/preview.conf)"
  [[ "$homelab" =~ ^[0-9A-Fa-f:./,]+$ ]] || die "PREVIEW_HOMELAB_PROXY invalide : « $homelab »"

  subnets=$(docker network inspect "$network" --format '{{range .IPAM.Config}}{{.Subnet}},{{end}}' 2>/dev/null) ||
    die "réseau Docker « $network » introuvable : il est créé une fois avec la LXC (deploy/preview/README.md)"
  [ -n "$subnets" ] || die "le réseau Docker « $network » n'a pas de sous-réseau"
  printf '%s%s' "$subnets" "$homelab"
}

diagnose() {
  local n="$1"
  log "Le déploiement a échoué. État du projet :"
  compose "$n" ps -a >&2 || true
  log "Dernières lignes des services concernés :"
  compose "$n" logs --no-color --tail 40 restore migrate api >&2 || true
  compose "$n" logs --no-color --tail 8 postgres >&2 || true
}

cmd_up() {
  local pull=1 n="" dump="" arg
  local positional=()
  for arg in "$@"; do
    case "$arg" in
    --no-pull) pull=0 ;;
    -*) die "option inconnue : $arg" ;;
    *) positional+=("$arg") ;;
    esac
  done
  n="${positional[0]:-}"
  dump="${positional[1]:-}"
  [ "${#positional[@]}" -le 2 ] || die "trop d'arguments"
  validate_pr "$n"
  [ -z "$dump" ] || [ -f "$dump" ] || die "dump introuvable : $dump"

  local network dir
  network=$(setting PREVIEW_NETWORK previews)
  dir=$(state_dir "$n")
  PREVIEW_PROXY_NETWORKS=$(proxy_networks "$network")
  export PREVIEW_PROXY_NETWORKS

  mkdir -p "$dir/dump"
  ensure_secrets "$dir"
  # Copie, pas déplacement : le fichier de l'appelant reste le sien (le workflow le supprime).
  # La copie, elle, est supprimée par dbtools après la restauration, et ici de toute façon.
  rm -f "$dir/dump/latest.dump"
  if [ -n "$dump" ]; then
    install -m 0600 "$dump" "$dir/dump/latest.dump"
  else
    log "Pas de dump : si la base n'a pas déjà été restaurée, elle garde ses données de démo."
  fi

  if [ "$pull" -eq 1 ]; then
    log "Pull des images (${IMAGE_TAG:-pr-$n})…"
    compose "$n" pull --quiet || {
      rm -f "$dir/dump/latest.dump"
      die "pull impossible — images absentes de GHCR ? Pour des images déjà présentes localement : --no-pull"
    }
  fi

  # Les images viennent d'être tirées : aucun pull de plus. Avec --no-pull, on garde celles qui
  # sont déjà là et seules les images absentes (PostgreSQL, la première fois) sont tirées.
  # Jamais de build.
  local pull_policy=never
  [ "$pull" -eq 1 ] || pull_policy=missing

  log "Démarrage de pr-$n (attente de l'API, $WAIT_TIMEOUT_SECONDS s au plus)…"
  if ! compose "$n" up -d --pull "$pull_policy" --remove-orphans --wait --wait-timeout "$WAIT_TIMEOUT_SECONDS" >&2; then
    diagnose "$n"
    rm -f "$dir/dump/latest.dump"
    exit 1
  fi
  rm -f "$dir/dump/latest.dump"

  date -u +%Y-%m-%dT%H:%M:%SZ >"$dir/last-deploy"
  local domain
  domain=$(setting PREVIEW_DOMAIN pr.houseflow.cloud)
  log "pr-$n déployée : https://$n.$domain (API : https://api-$n.$domain)"
}

cmd_down() {
  local n="${1:-}" name image dir
  validate_pr "$n"
  dir=$(state_dir "$n")
  log "Suppression de pr-$n…"
  # `down` d'un projet absent n'est pas une erreur : la commande est idempotente.
  compose "$n" down -v --remove-orphans >&2
  for name in "${IMAGE_NAMES[@]}"; do
    image="$IMAGE_REPO-$name:pr-$n"
    docker image rm "$image" >/dev/null 2>&1 || true
  done
  rm -rf "$dir"
  log "pr-$n supprimée."
}

# Date du dernier déploiement (epoch) : le fichier d'état, à défaut la date du répertoire
# (premier déploiement qui n'a pas abouti).
last_deploy_epoch() {
  local dir="$1" stamp=""
  [ ! -f "$dir/last-deploy" ] || stamp=$(cat "$dir/last-deploy")
  date -d "$stamp" +%s 2>/dev/null || stat -c %Y "$dir"
}

cmd_ls() {
  local dir n stamp running found=0
  for dir in "$PREVIEW_ROOT"/pr-*/; do
    [ -d "$dir" ] || continue
    dir="${dir%/}"
    n="${dir##*/pr-}"
    stamp="-"
    [ ! -f "$dir/last-deploy" ] || stamp=$(cat "$dir/last-deploy")
    running=$(docker ps -q --filter "label=com.docker.compose.project=pr-$n" | wc -l | tr -d ' ')
    printf 'pr-%s\t%s\t%s conteneur(s) en marche\n' "$n" "$stamp" "$running"
    found=1
  done
  [ "$found" -eq 1 ] || log "Aucune preview."
}

# Les images `houseflow-preview-*` qu'aucun conteneur n'utilise plus (tags de PR fermées, versions
# remplacées par un `pull`) : sans cela, le disque de la LXC ne ferait que grossir.
prune_images() {
  local ref
  while read -r ref; do
    case "$ref" in '' | *:'<none>') continue ;; esac
    [ -z "$(docker ps -aq --filter "ancestor=$ref")" ] || continue
    docker image rm "$ref" >/dev/null 2>&1 && log "Image supprimée : $ref"
  done < <(docker image ls --format '{{.Repository}}:{{.Tag}}' --filter "reference=$IMAGE_REPO-*")
  # Couches des tags remplacés par un `pull`, devenues anonymes.
  docker image prune -f >/dev/null
}

cmd_reap() {
  local ttl_hours="${1:-}" dir n now age_limit last
  [[ "$ttl_hours" =~ ^[0-9]+$ ]] || die "usage : preview.sh reap <ttl_heures> (entier)"
  now=$(date +%s)
  age_limit=$((ttl_hours * 3600))
  for dir in "$PREVIEW_ROOT"/pr-*/; do
    [ -d "$dir" ] || continue
    dir="${dir%/}"
    n="${dir##*/pr-}"
    [[ "$n" =~ ^[1-9][0-9]*$ ]] || continue
    last=$(last_deploy_epoch "$dir")
    if [ $((now - last)) -gt "$age_limit" ]; then
      log "pr-$n : dernier déploiement il y a plus de $ttl_hours h."
      cmd_down "$n"
    fi
  done
  prune_images
}

usage() {
  cat >&2 <<'USAGE'
usage : preview.sh <commande>

  up [--no-pull] <n> [<dump>]   déploie la preview de la PR <n> (images tirées de GHCR, dump local à restaurer)
  down <n>                      supprime la preview de la PR <n>
  ls                            liste les previews
  reap <ttl_heures>             supprime les previews plus vieilles que le TTL, purge les images inutiles

Réglages : PREVIEW_ROOT, PREVIEW_HOMELAB_PROXY, PREVIEW_NETWORK, PREVIEW_DOMAIN, IMAGE_TAG
(voir l'en-tête du script et deploy/preview/README.md).
USAGE
}

main() {
  local command="${1:-}"
  [ "$#" -eq 0 ] || shift
  case "$command" in
  up | down | reap)
    require_tools
    acquire_lock
    "cmd_$command" "$@"
    ;;
  ls)
    require_tools
    cmd_ls
    ;;
  -h | --help | help) usage ;;
  "")
    usage
    exit 2
    ;;
  *)
    usage
    die "commande inconnue : « $command »"
    ;;
  esac
}

main "$@"
