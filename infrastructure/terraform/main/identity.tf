# ── Data source for current Azure config ─────────────

data "azurerm_client_config" "current" {}

# ── User-Assigned Managed Identity ───────────────────
# Shared by all Container Apps (prod, preprod, ephemeral)
# for passwordless PostgreSQL authentication via Entra ID.

resource "azurerm_user_assigned_identity" "main" {
  name                = "id-${var.project}"
  location            = var.location
  resource_group_name = data.azurerm_resource_group.main.name
}
