# ── Database ──────────────────────────────────────────

resource "azurerm_postgresql_flexible_server_database" "prod" {
  name      = "${var.project}_prod"
  server_id = local.main.pg_server_id
  charset   = "UTF8"
  collation = "en_US.utf8"
}

resource "azurerm_management_lock" "db_prod" {
  name       = "no-delete-db-prod"
  scope      = azurerm_postgresql_flexible_server_database.prod.id
  lock_level = "CanNotDelete"
  notes      = "Protect production database from accidental deletion"
}

locals {
  pg_connection_prod = join(";", [
    "Host=${local.main.pg_host}",
    "Port=5432",
    "Database=${azurerm_postgresql_flexible_server_database.prod.name}",
    "Username=${local.main.identity_name}",
    "SSL Mode=Require",
    "Trust Server Certificate=true",
  ])
}
