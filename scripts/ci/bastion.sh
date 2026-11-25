#!/usr/bin/env bash
# Drives the production SSH bastion (#384): stopped by default, started on demand.
#
# The bastion is a Container App (`ca-bastion-prod`). Its native `Stopped` state is not an
# attribute of `azurerm_container_app`: Terraform does not see it (no drift), and a stopped
# app has no replica, is not billed and does NOT wake up on an inbound connection
# (unlike scale-to-zero, which restarted on any port scan).
#
# Usage: bastion.sh <command>
#   status            prints the running status (Running / Stopped...) in the log
#   start             starts the bastion and publishes the tunnel command in the run summary
#   stop              stops the bastion (fails if the Stopped state is not observed)
#   ensure-stopped    stops the bastion UNLESS a `Bastion start` run is in progress; no-op
#                     if the app does not exist (instance without a bastion)
#   watchdog          like ensure-stopped, but fails if the bastion was running outside a
#                     session (the red run makes the anomaly visible)
#
# Requirements: `az` logged in (azure/login), `containerapp` extension, `gh` + GH_TOKEN
# (`actions: read` permission) for ensure-stopped / watchdog.
# Variables: BASTION_RG, BASTION_APP, PG_SERVER (defaults: production),
#            BASTION_SESSION_MINUTES (announced duration, default 55), BASTION_RUN_TITLE.
set -euo pipefail

RG="${BASTION_RG:-rg-houseflow-prod}"
APP="${BASTION_APP:-ca-bastion-prod}"
PG_SERVER="${PG_SERVER:-psql-houseflow-prod}"
SESSION_MINUTES="${BASTION_SESSION_MINUTES:-55}"
# `run-name` of bastion.yml for the start action: used to recognise a session in progress.
RUN_TITLE="${BASTION_RUN_TITLE:-Bastion start}"

summary() { if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then echo "$*" >>"$GITHUB_STEP_SUMMARY"; else echo "$*"; fi; }

app_exists() {
  az containerapp show --name "$APP" --resource-group "$RG" --query name -o tsv >/dev/null 2>&1
}

running_status() {
  az containerapp show --name "$APP" --resource-group "$RG" \
    --query properties.runningStatus -o tsv
}

# Waits for the wanted state (a stop can go through `Progressing`): 2 minutes at most.
wait_for_status() {
  local wanted="$1" current="" i
  for i in $(seq 1 24); do
    current="$(running_status)"
    [ "$current" = "$wanted" ] && return 0
    sleep 5
  done
  echo "::error::$APP is in state '$current' instead of '$wanted' after 2 minutes."
  return 1
}

do_stop() {
  az containerapp stop --name "$APP" --resource-group "$RG" -o none
  wait_for_status Stopped
  echo "$APP : Stopped."
}

session_running() {
  # A `Bastion start` run in progress enforces the one-hour limit itself: do not interrupt it.
  local n
  n="$(gh run list --repo "$GITHUB_REPOSITORY" --workflow bastion.yml --status in_progress \
        --json displayTitle --jq "[.[] | select(.displayTitle == \"$RUN_TITLE\")] | length")"
  [ "${n:-0}" -gt 0 ]
}

cmd="${1:-}"
case "$cmd" in
  status)
    if ! app_exists; then echo "$APP does not exist in $RG."; exit 1; fi
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
    summary "### Bastion started"
    summary ""
    summary "Automatic stop by **$stop_at** at the latest (cancelling this run stops it right away)."
    summary "The first connection takes about thirty seconds (cold start)."
    summary ""
    summary '```'
    summary "ssh -i <private-key> -N -L 5432:${pg}:5432 bastion@${fqdn} -p 2222"
    summary '```'
    ;;

  stop)
    if ! app_exists; then echo "$APP does not exist in $RG."; exit 1; fi
    do_stop
    summary "### Bastion stopped (\`Stopped\`)"
    ;;

  ensure-stopped | watchdog)
    if ! app_exists; then
      echo "::notice::$APP does not exist in $RG: nothing to stop."
      exit 0
    fi
    status="$(running_status)"
    if [ "$status" = "Stopped" ]; then
      echo "$APP : already Stopped."
      summary "### Bastion : \`Stopped\`"
      exit 0
    fi
    if session_running; then
      echo "::notice::$APP: $status, a \"$RUN_TITLE\" session is in progress — it will stop it."
      exit 0
    fi
    echo "$APP: $status with no session in progress, stopping."
    do_stop
    if [ "$cmd" = "watchdog" ]; then
      echo "::error::The bastion was running ($status) with no \"$RUN_TITLE\" session: it has just been stopped. Possible cause: started outside the workflow (portal, CLI), or run killed before its stop step."
      summary "### Bastion found in state \`$status\` outside a session — stopped"
      exit 1
    fi
    summary "### Bastion stopped (\`Stopped\`)"
    ;;

  *)
    echo "Usage: $0 status|start|stop|ensure-stopped|watchdog" >&2
    exit 2
    ;;
esac
