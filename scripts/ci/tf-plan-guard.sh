#!/usr/bin/env bash
#
# tf-plan-guard.sh <protected|dns> <plan.json>
#
# Garde-fou sur un plan Terraform sérialisé (`terraform show -json tfplan`),
# à interposer entre le `plan -out` et l'`apply tfplan`.
#
#   protected  Refuse un plan qui détruit une ressource d'un type protégé
#              (serveur PostgreSQL et ses bases, Key Vault, VNet, subnet, CAE,
#              identités managées, workspace Log Analytics). Un state vide, une
#              ressource renommée ou un `moved` oublié planifieraient leur
#              destruction : mieux vaut un apply rouge qu'une base effacée.
#
#   dns        Refuse un plan qui supprime des enregistrements OVH, sauf
#              marqueur [dns-allow-destroy] dans HEAD_COMMIT_MESSAGE ou
#              ALLOW_DNS_DESTROY=true. Les TXT asuid.* portent la validation de
#              chaque domaine custom : les perdre casse tous les bindings.

set -euo pipefail

MODE="${1:-}"
PLAN_JSON="${2:-}"
if [ -z "$MODE" ] || [ -z "$PLAN_JSON" ]; then
  echo "usage: $0 <protected|dns> <plan.json>" >&2
  exit 2
fi
[ -f "$PLAN_JSON" ] || {
  echo "::error::plan JSON introuvable : $PLAN_JSON" >&2
  exit 1
}

case "$MODE" in
protected)
  PROTECTED='^azurerm_(container_app_environment|postgresql_flexible_server|postgresql_flexible_server_database|key_vault|virtual_network|subnet|user_assigned_identity|log_analytics_workspace)$'
  BAD=$(jq -r --arg re "$PROTECTED" \
    '.resource_changes[]? | select(.type | test($re)) | select(.change.actions | index("delete")) | .address' \
    "$PLAN_JSON")
  if [ -n "$BAD" ]; then
    echo "::error::Apply refusé : le plan détruit une ressource protégée :"
    echo "$BAD"
    exit 1
  fi
  echo "Garde-fou : aucune ressource protégée détruite."
  ;;

dns)
  DESTROYED=$(jq -r \
    '.resource_changes[]? | select(.type == "ovh_domain_zone_record") | select(.change.actions | index("delete")) | .address' \
    "$PLAN_JSON")
  ALLOW="${ALLOW_DNS_DESTROY:-false}"
  case "${HEAD_COMMIT_MESSAGE:-}" in *"[dns-allow-destroy]"*) ALLOW=true ;; esac
  if [ -n "$DESTROYED" ] && [ "$ALLOW" != "true" ]; then
    echo "::error::Apply refusé : le plan supprime des enregistrements DNS. Marquer le commit [dns-allow-destroy] ou lancer avec allow_dns_destroy si c'est voulu :"
    echo "$DESTROYED"
    exit 1
  fi
  if [ -n "$DESTROYED" ]; then
    echo "Suppression d'enregistrements DNS explicitement autorisée :"
    echo "$DESTROYED"
  else
    echo "Garde-fou : aucun enregistrement DNS supprimé."
  fi
  ;;

*)
  echo "::error::mode inconnu : $MODE (protected|dns)" >&2
  exit 2
  ;;
esac
