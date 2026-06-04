# ── Database ──────────────────────────────────────────

resource "azurerm_postgresql_flexible_server_database" "preprod" {
  name      = "${var.project}_preprod"
  server_id = local.main.pg_server_id
  charset   = "UTF8"
  collation = "en_US.utf8"
}

resource "azurerm_management_lock" "db_preprod" {
  name       = "no-delete-db-preprod"
  scope      = azurerm_postgresql_flexible_server_database.preprod.id
  lock_level = "CanNotDelete"
  notes      = "Protect preprod database from accidental deletion"
}

locals {
  pg_connection_preprod = join(";", [
    "Host=${local.main.pg_host}",
    "Port=5432",
    "Database=${azurerm_postgresql_flexible_server_database.preprod.name}",
    "Username=${local.main.identity_name}",
    "SSL Mode=Require",
    "Trust Server Certificate=true",
  ])
}
