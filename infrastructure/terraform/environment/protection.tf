# ── Protections des instances permanentes ────────────
#
# Conditionnées par `rg_lock_enabled`, donc absentes des environnements
# jetables — un lock CanNotDelete sur un resource group éphémère empêcherait
# le reaper de faire son travail, ce qui est exactement le risque financier
# que ce design cherche à écarter.

resource "azurerm_management_lock" "resource_group" {
  count = var.rg_lock_enabled ? 1 : 0

  name       = "lock-${local.resource_group_name}"
  scope      = azurerm_resource_group.env.id
  lock_level = "CanNotDelete"
  notes      = "Environnement permanent — suppression interdite"
}

resource "azurerm_management_lock" "database" {
  count = var.rg_lock_enabled ? 1 : 0

  name       = "lock-${local.database_name}"
  scope      = azurerm_postgresql_flexible_server_database.env.id
  lock_level = "CanNotDelete"
  notes      = "Base de production — suppression interdite"
}
