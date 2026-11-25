#!/usr/bin/env bash
# Pilote le bastion SSH de production (#384) : arrêté par défaut, démarré à la demande.
#
# Le bastion est un Container App (`ca-bastion-prod`). Son état `Stopped` natif n'est pas un
# attribut du `azurerm_container_app` : Terraform ne le voit pas (aucune dérive), et une app
# arrêtée n'a aucun réplica, n'est pas facturée et NE SE RÉVEILLE PAS sur une connexion
# entrante (contrairement au scale-to-zero, qui redémarrait sur n'importe quel scan du port).
#
# Usage : bastion.sh <commande>
#   status            affiche l'état d'exécution (Running / Stopped…) dans le journal
#   start             démarre le bastion et publie dans le résumé du run la commande du tunnel
#   stop              arrête le bastion (échoue si l'état Stopped n'est pas constaté)
#   ensure-stopped    arrête le bastion SAUF si un run `Bastion start` est en cours ; sans
#                     effet si l'app n'existe pas (instance sans bastion)
#   watchdog          comme ensure-stopped, mais échoue si le bastion tournait hors session
#                     (le run en rouge rend l'anomalie visible)
#
# Prérequis : `az` connecté (azure/login), extension `containerapp`, `gh` + GH_TOKEN
# (permission `actions: read`) pour ensure-stopped / watchdog.
# Variables : BASTION_RG, BASTION_APP, PG_SERVER (défauts : la production),
#             BASTION_SESSION_MINUTES (durée annoncée, défaut 55), BASTION_RUN_TITLE.
set -euo pipefail

RG="${BASTION_RG:-rg-houseflow-prod}"
APP="${BASTION_APP:-ca-bastion-prod}"
PG_SERVER="${PG_SERVER:-psql-houseflow-prod}"
SESSION_MINUTES="${BASTION_SESSION_MINUTES:-55}"
# `run-name` de bastion.yml pour l'action start : sert à reconnaître une session en cours.
RUN_TITLE="${BASTION_RUN_TITLE:-Bastion start}"

summary() { if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then echo "$*" >>"$GITHUB_STEP_SUMMARY"; else echo "$*"; fi; }

app_exists() {
  az containerapp show --name "$APP" --resource-group "$RG" --query name -o tsv >/dev/null 2>&1
}

running_status() {
  az containerapp show --name "$APP" --resource-group "$RG" \
    --query properties.runningStatus -o tsv
}

# Attend l'état voulu (l'arrêt peut passer par `Progressing`) : 2 minutes au plus.
wait_for_status() {
  local wanted="$1" current="" i
  for i in $(seq 1 24); do
    current="$(running_status)"
    [ "$current" = "$wanted" ] && return 0
    sleep 5
  done
  echo "::error::$APP est en état '$current' au lieu de '$wanted' après 2 minutes."
  return 1
}

do_stop() {
  az containerapp stop --name "$APP" --resource-group "$RG" -o none
  wait_for_status Stopped
  echo "$APP : Stopped."
}

session_running() {
  # Un run `Bastion start` en cours tient lui-même la borne d'une heure : on ne l'interrompt pas.
  local n
  n="$(gh run list --repo "$GITHUB_REPOSITORY" --workflow bastion.yml --status in_progress \
        --json displayTitle --jq "[.[] | select(.displayTitle == \"$RUN_TITLE\")] | length")"
  [ "${n:-0}" -gt 0 ]
}

cmd="${1:-}"
case "$cmd" in
  status)
    if ! app_exists; then echo "$APP n'existe pas dans $RG."; exit 1; fi
    status="$(running_status)"
    echo "$APP : $status"
    summary "### Bastion : \`$status\`"
    ;;

  start)
    az containerapp start --name "$APP" --resource-group "$RG" -o none
    wait_for_status Running
    fqdn="$(az containerapp show --name "$APP" --resource-group "$RG" \
              --query properties.configuration.ingress.fqdn -o tsv)"
    pg="$(az postgres flexible-server show --name "$PG_SERVER" --resource-group "$RG" \
            --query fullyQualifiedDomainName -o tsv 2>/dev/null || echo '<pg_fqdn>')"
    stop_at="$(date -u -d "+${SESSION_MINUTES} minutes" '+%H:%M UTC')"
    summary "### Bastion démarré"
    summary ""
    summary "Arrêt automatique au plus tard à **$stop_at** (annuler ce run l'arrête tout de suite)."
    summary "La première connexion prend une trentaine de secondes (démarrage à froid)."
    summary ""
    summary '```'
    summary "ssh -i <clé-privée> -N -L 5432:${pg}:5432 bastion@${fqdn} -p 2222"
    summary '```'
    ;;

  stop)
    if ! app_exists; then echo "$APP n'existe pas dans $RG."; exit 1; fi
    do_stop
    summary "### Bastion arrêté (\`Stopped\`)"
    ;;

  ensure-stopped | watchdog)
    if ! app_exists; then
      echo "::notice::$APP n'existe pas dans $RG : rien à arrêter."
      exit 0
    fi
    status="$(running_status)"
    if [ "$status" = "Stopped" ]; then
      echo "$APP : déjà Stopped."
      summary "### Bastion : \`Stopped\`"
      exit 0
    fi
    if session_running; then
      echo "::notice::$APP : $status, une session « $RUN_TITLE » est en cours — elle l'arrêtera."
      exit 0
    fi
    echo "$APP : $status sans session en cours, arrêt."
    do_stop
    if [ "$cmd" = "watchdog" ]; then
      echo "::error::Le bastion tournait ($status) sans session « $RUN_TITLE » : il vient d'être arrêté. Cause possible : démarrage hors workflow (portail, CLI), run tué avant son arrêt."
      summary "### Bastion trouvé en état \`$status\` hors session — arrêté"
      exit 1
    fi
    summary "### Bastion arrêté (\`Stopped\`)"
    ;;

  *)
    echo "Usage : $0 status|start|stop|ensure-stopped|watchdog" >&2
    exit 2
    ;;
esac
