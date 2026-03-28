#!/usr/bin/env bash
set -euo pipefail

# ── Terraform State Migration Script ─────────────────
# Splits the single houseflow.tfstate into two isolated states:
#   - main.tfstate    : permanent infrastructure (postgres, CAE, network, etc.)
#   - ephemeral.tfstate : PR preview environments (container apps + PR databases)
#
# Prerequisites:
#   - Azure CLI logged in (az login)
#   - ARM_* environment variables set for OIDC auth
#   - No active Terraform operations (state lock must be free)
#
# Usage:
#   cd infrastructure/terraform
#   bash migrate-state.sh
#
# This script is idempotent — it checks if migration is needed before running.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MAIN_DIR="$SCRIPT_DIR/main"
EPHEMERAL_DIR="$SCRIPT_DIR/ephemeral"

echo "=== Step 1: Initialize both new backends ==="
echo ""

echo "Initializing main/ backend..."
cd "$MAIN_DIR"
terraform init -input=false \
  -backend-config="key=main.tfstate" \
  -migrate-state <<< "yes" 2>/dev/null || terraform init -input=false

echo ""
echo "Initializing ephemeral/ backend..."
cd "$EPHEMERAL_DIR"
terraform init -input=false

echo ""
echo "=== Step 2: Check for PR environments to migrate ==="

cd "$MAIN_DIR"

# Find any PR-related resources in the main state
PR_RESOURCES=$(terraform state list 2>/dev/null | grep -E '(module\.pr_env|azurerm_postgresql_flexible_server_database\.pr)' || true)

if [ -z "$PR_RESOURCES" ]; then
  echo "No PR environment resources found in main state."
  echo "Migration complete — nothing to move."
  exit 0
fi

echo "Found PR resources to migrate:"
echo "$PR_RESOURCES"
echo ""

echo "=== Step 3: Move PR resources from main → ephemeral ==="

# Move each resource individually
while IFS= read -r resource; do
  echo "Moving: $resource"
  terraform state mv \
    -state="$MAIN_DIR/terraform.tfstate" \
    -state-out="$EPHEMERAL_DIR/terraform.tfstate" \
    "$resource" "$resource" 2>/dev/null || {
      echo "  ⚠ Could not move $resource (may not exist in state or already moved)"
    }
done <<< "$PR_RESOURCES"

echo ""
echo "=== Step 4: Push ephemeral state to remote backend ==="

cd "$EPHEMERAL_DIR"
# Re-init to push the local state to the remote backend
terraform init -input=false -migrate-state <<< "yes" 2>/dev/null || true

echo ""
echo "=== Migration complete ==="
echo ""
echo "Verify with:"
echo "  cd main && terraform plan     # Should show no changes"
echo "  cd ephemeral && terraform plan # Should show no changes"
