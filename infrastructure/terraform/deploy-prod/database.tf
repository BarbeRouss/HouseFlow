# ── Database ──────────────────────────────────────────
#
# Seule base créée par ARM : `id-houseflow-prod` est administrateur Entra du
# serveur, les autres bases sont créées en SQL par les jobs dbtools.

resource "azurerm_postgresql_flexible_server_database" "prod" {
  name      = "${var.project}_prod"
  server_id = data.azurerm_postgresql_flexible_server.shared.id
  charset   = "UTF8"
  collation = "en_US.utf8"
}

resource "azurerm_management_lock" "prod_database" {
  name       = "lock-houseflow-prod-db"
  scope      = azurerm_postgresql_flexible_server_database.prod.id
  lock_level = "CanNotDelete"
  notes      = "Base de production — suppression interdite"
}

locals {
  pg_connection_prod = join(";", [
    "Host=${data.azurerm_postgresql_flexible_server.shared.fqdn}",
    "Port=5432",
    "Database=${azurerm_postgresql_flexible_server_database.prod.name}",
    "Username=${data.azurerm_user_assigned_identity.prod.name}",
    "SSL Mode=Require",
    "Trust Server Certificate=true",
  ])
}
