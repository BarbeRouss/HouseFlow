#!/bin/bash
set -euo pipefail

# Add a resource type to the Azure Policy "HouseFlow - Types de ressources autorises"
# Usage: ./add-allowed-resource-type.sh <resource-type>
# Example: ./add-allowed-resource-type.sh "Microsoft.App/managedEnvironments/managedCertificates"

ASSIGNMENT_NAME="houseflow-allowed-resources"

if [[ $# -lt 1 ]]; then
  echo "Usage: $0 <resource-type> [<resource-type> ...]"
  echo "Example: $0 \"Microsoft.App/managedEnvironments/managedCertificates\""
  exit 1
fi

# Ensure Azure CLI is available and logged in
if ! az account show &>/dev/null; then
  echo "Error: Not logged in to Azure CLI. Run 'az login' first."
  exit 1
fi

# Get current allowed types from the policy assignment
echo "Fetching current policy assignment..."
CURRENT=$(az policy assignment show \
  --name "$ASSIGNMENT_NAME" \
  --query "parameters.listOfResourceTypesAllowed.value" \
  -o json)

UPDATED="$CURRENT"
for RESOURCE_TYPE in "$@"; do
  # Check if already present
  if echo "$UPDATED" | grep -q "\"$RESOURCE_TYPE\""; then
    echo "\"$RESOURCE_TYPE\" is already in the allowed list, skipping."
    continue
  fi

  echo "Adding \"$RESOURCE_TYPE\"..."
  UPDATED=$(echo "$UPDATED" | jq --arg rt "$RESOURCE_TYPE" '. + [$rt]')
done

# Update the policy assignment
echo "Updating policy assignment..."
az policy assignment update \
  --name "$ASSIGNMENT_NAME" \
  --params "{\"listOfResourceTypesAllowed\": {\"value\": $UPDATED}}" \
  --output none

echo "Done. Current allowed types:"
az policy assignment show \
  --name "$ASSIGNMENT_NAME" \
  --query "parameters.listOfResourceTypesAllowed.value" \
  -o tsv
