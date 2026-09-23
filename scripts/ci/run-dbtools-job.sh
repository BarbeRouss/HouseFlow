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
# Avec DBTOOLS_LOG_WORKSPACE (customer id du workspace Log Analytics du CAE), les
# lignes écrites par le job sont recopiées ici une fois l'exécution terminée — succès
# ou échec. Sans elles, le workflow ne montrait qu'un statut, jamais ce que le job
# avait fait.
#
# Réglages : DBTOOLS_JOB_TIMEOUT (900 s), DBTOOLS_JOB_POLL_INTERVAL (10 s),
#            DBTOOLS_LOG_WAIT (300 s), DBTOOLS_LOG_POLL (15 s).

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
LOG_WORKSPACE="${DBTOOLS_LOG_WORKSPACE:-}"
LOG_WAIT="${DBTOOLS_LOG_WAIT:-300}"

# Dernières lignes possibles de `dbtools` : fin normale, restauration déjà faite,
# absence de dump, ou erreur. Tant qu'aucune n'est ingérée, la sortie est incomplète.
readonly LOG_END='Terminé|déjà été restaurée|AVERTISSEMENT : aucun|ERREUR'

# Recopie les logs du job depuis Log Analytics. Jamais bloquant : un log manquant ne
# doit pas faire échouer une exécution réussie.
print_job_logs() {
  local execution="$1" query logs="" deadline
  if [ -z "$LOG_WORKSPACE" ]; then
    echo "Logs : az containerapp job logs show -n $JOB_NAME -g $RESOURCE_GROUP --execution $execution"
    return 0
  fi
  # Le nom de l'exécution est interpolé dans du KQL entre apostrophes.
  [[ "$execution" =~ ^[A-Za-z0-9-]+$ ]] || return 0
  query="ContainerAppConsoleLogs_CL | where ContainerGroupName_s startswith '$execution' | order by TimeGenerated asc | project Log_s"

  az extension add --name log-analytics --upgrade --yes --only-show-errors -o none 2>/dev/null || true

  # L'ingestion dans Log Analytics prend d'une à quelques minutes.
  local raw error=""
  deadline=$(($(date +%s) + LOG_WAIT))
  while :; do
    if raw=$(az monitor log-analytics query --workspace "$LOG_WORKSPACE" \
      --analytics-query "$query" -o json 2>&1); then
      logs=$(printf '%s' "$raw" | jq -r '.[].Log_s' 2>/dev/null || true)
      error=""
    else
      error=$(printf '%s' "$raw" | tail -n 1)
    fi
    if printf '%s' "$logs" | grep -Eq "$LOG_END"; then
      break
    fi
    if [ "$(date +%s)" -ge "$deadline" ]; then
      echo "::warning::Logs de $execution incomplets après ${LOG_WAIT}s${error:+ — dernière erreur : $error}"
      break
    fi
    sleep "${DBTOOLS_LOG_POLL:-15}"
  done

  echo "::group::Logs de $JOB_NAME / $execution"
  printf '%s\n' "${logs:-(aucune ligne ingérée)}"
  echo "::endgroup::"
  # Le résumé en clair hors du groupe replié : c'est ce qu'on vient chercher.
  printf '%s\n' "$logs" | grep -E "Importé|$LOG_END" || true
}

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
    print_job_logs "$execution_name"
    exit 0
    ;;
  Failed | Degraded | Cancelled)
    echo "::error::$JOB_NAME / $execution_name : $status"
    print_job_logs "$execution_name"
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
