#!/usr/bin/env bash
#
# run-dbtools-job.sh <job-name> <resource-group> [VAR=VALEUR …]
#
# Démarre un Container Apps Job dbtools et attend la fin de SON exécution.
# `az containerapp job start` rend la main dès que l'exécution est acceptée :
# sans cette attente, le workflow enchaînerait sur la fumée alors que la base
# n'est pas encore restaurée, et un job en échec passerait inaperçu.
#
# Sort en erreur si l'exécution échoue ou dépasse DBTOOLS_JOB_TIMEOUT secondes.
#
# Réglages : DBTOOLS_JOB_TIMEOUT (900 s), DBTOOLS_JOB_POLL_INTERVAL (10 s).

set -euo pipefail

JOB_NAME="${1:-}"
RESOURCE_GROUP="${2:-}"
if [ -z "$JOB_NAME" ] || [ -z "$RESOURCE_GROUP" ]; then
  echo "usage: $0 <job-name> <resource-group> [VAR=VALEUR …]" >&2
  exit 2
fi
shift 2

TIMEOUT="${DBTOOLS_JOB_TIMEOUT:-900}"
INTERVAL="${DBTOOLS_JOB_POLL_INTERVAL:-10}"

az extension add --name containerapp --upgrade --yes --only-show-errors -o none 2>/dev/null || true

start_args=(--name "$JOB_NAME" --resource-group "$RESOURCE_GROUP" -o json)
if [ "$#" -gt 0 ]; then
  start_args+=(--env-vars "$@")
  echo "Démarrage de $JOB_NAME dans $RESOURCE_GROUP (env : $*)…"
else
  echo "Démarrage de $JOB_NAME dans $RESOURCE_GROUP…"
fi

started=$(az containerapp job start "${start_args[@]}")
execution_name=$(printf '%s' "$started" | jq -r '.name // empty')
if [ -z "$execution_name" ]; then
  echo "::error::Nom de l'exécution introuvable dans la réponse de « az containerapp job start »"
  printf '%s\n' "$started"
  exit 1
fi
echo "Exécution : $execution_name"

deadline=$(($(date +%s) + TIMEOUT))
while :; do
  status=$(az containerapp job execution show \
    --name "$JOB_NAME" --resource-group "$RESOURCE_GROUP" \
    --job-execution-name "$execution_name" \
    --query properties.status -o tsv 2>/dev/null || echo "")

  case "$status" in
  Succeeded)
    echo "$JOB_NAME / $execution_name : Succeeded"
    exit 0
    ;;
  Failed | Degraded | Cancelled)
    echo "::error::$JOB_NAME / $execution_name : $status"
    az containerapp job execution show \
      --name "$JOB_NAME" --resource-group "$RESOURCE_GROUP" \
      --job-execution-name "$execution_name" -o json 2>/dev/null || true
    echo "Logs : az containerapp job logs show -n $JOB_NAME -g $RESOURCE_GROUP --execution $execution_name"
    exit 1
    ;;
  "")
    # L'exécution peut n'être pas encore visible juste après le start.
    echo "  … statut indisponible, nouvelle interrogation"
    ;;
  *)
    echo "  … $status"
    ;;
  esac

  if [ "$(date +%s)" -ge "$deadline" ]; then
    echo "::error::$JOB_NAME / $execution_name : toujours « ${status:-inconnu} » après ${TIMEOUT}s"
    echo "Logs : az containerapp job logs show -n $JOB_NAME -g $RESOURCE_GROUP --execution $execution_name"
    exit 1
  fi
  sleep "$INTERVAL"
done
