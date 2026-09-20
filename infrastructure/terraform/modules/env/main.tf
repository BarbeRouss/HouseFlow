# ── Références croisées : data sources sur noms fixes ──────────
# Jamais de terraform_remote_state : un service principal ne lit que son state.

data "azurerm_resource_group" "env" {
  name = var.resource_group_name
}

data "azurerm_subnet" "cae" {
  name                 = "snet-cae-${var.env}"
  virtual_network_name = var.shared_vnet_name
  resource_group_name  = var.shared_resource_group_name
}

data "azurerm_key_vault" "main" {
  name                = var.key_vault_name
  resource_group_name = var.shared_resource_group_name
}

data "azurerm_user_assigned_identity" "env" {
  name                = "id-houseflow-${var.env}"
  resource_group_name = var.shared_resource_group_name
}

data "azurerm_postgresql_flexible_server" "main" {
  name                = var.pg_server_name
  resource_group_name = var.shared_resource_group_name
}

resource "azurerm_management_lock" "resource_group" {
  count = var.rg_lock_enabled ? 1 : 0

  name       = "lock-${var.resource_group_name}"
  scope      = data.azurerm_resource_group.env.id
  lock_level = "CanNotDelete"
  notes      = "Environnement de production — suppression interdite"
}
