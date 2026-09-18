#!/bin/bash
# Crée (ou met à jour) le rôle « HouseFlow Deployer (subscription) » et l'assigne
# au service principal de l'app OIDC GitHub à l'échelle de la souscription.
#
# Usage : remplacer <SUBSCRIPTION_ID> ci-dessous (ou exporter SUBSCRIPTION_ID),
# puis `bash infrastructure/rbac/assign-deployer-subscription-role.sh`.
# Idempotent : relançable sans effet si le rôle et l'assignation existent déjà.
set -euo pipefail

SUBSCRIPTION_ID="${SUBSCRIPTION_ID:-<SUBSCRIPTION_ID>}"
ROLE_NAME="HouseFlow Deployer (subscription)"
ROLE_FILE="$(cd "$(dirname "$0")" && pwd)/houseflow-deployer-subscription.role.json"
SP_DISPLAY_NAME="houseflow-github-actions"

case "$SUBSCRIPTION_ID" in *"<"*) echo "Renseigner SUBSCRIPTION_ID (ou remplacer <SUBSCRIPTION_ID> dans le script)." >&2; exit 1;; esac

ROLE_DEFINITION="$(sed "s#<SUBSCRIPTION_ID>#$SUBSCRIPTION_ID#" "$ROLE_FILE")"
if az role definition list --name "$ROLE_NAME" --scope "/subscriptions/$SUBSCRIPTION_ID" --query '[0].roleName' -o tsv | grep -q .; then
  az role definition update --role-definition "$ROLE_DEFINITION" -o none
  echo "Rôle « $ROLE_NAME » mis à jour."
else
  az role definition create --role-definition "$ROLE_DEFINITION" -o none
  echo "Rôle « $ROLE_NAME » créé."
fi

SP_OBJECT_ID="${SP_OBJECT_ID:-$(az ad sp list --display-name "$SP_DISPLAY_NAME" --query '[0].id' -o tsv)}"
[ -n "$SP_OBJECT_ID" ] || { echo "Service principal « $SP_DISPLAY_NAME » introuvable — exporter SP_OBJECT_ID." >&2; exit 1; }

az role assignment create \
  --role "$ROLE_NAME" \
  --scope "/subscriptions/$SUBSCRIPTION_ID" \
  --assignee-object-id "$SP_OBJECT_ID" \
  --assignee-principal-type ServicePrincipal \
  -o none
echo "Assignation faite : $SP_DISPLAY_NAME ($SP_OBJECT_ID) → « $ROLE_NAME » sur /subscriptions/$SUBSCRIPTION_ID."
