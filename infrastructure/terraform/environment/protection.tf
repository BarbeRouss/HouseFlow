# ── Protections des instances permanentes ────────────
#
# Absentes des environnements jetables, et pas par convention : le verrou se
# déduit de l'absence d'échéance, si bien qu'un environnement éphémère ne peut
# pas en porter. Un lock CanNotDelete sur un resource group promis au reaper
# l'empêcherait de faire son travail — exactement le risque financier que ce
# design écarte.

resource "azurerm_management_lock" "resource_group" {
  count = local.rg_lock_enabled ? 1 : 0

  name       = "lock-${local.resource_group_name}"
  scope      = azurerm_resource_group.env.id
  lock_level = "CanNotDelete"
  notes      = "Environnement permanent — suppression interdite"
}

resource "azurerm_management_lock" "database" {
  count = local.rg_lock_enabled ? 1 : 0

  name       = "lock-${local.database_name}"
  scope      = azurerm_postgresql_flexible_server_database.env.id
  lock_level = "CanNotDelete"
  notes      = "Base de production — suppression interdite"
}
