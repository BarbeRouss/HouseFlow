data "azurerm_resource_group" "main" {
  name = "rg-${var.project}"
}

# Locks on critical resources only (prod + DB).
# Not on the RG — that would block PR preview cleanup.

resource "azurerm_management_lock" "db_prod" {
  name       = "no-delete-db-prod"
  scope      = azurerm_postgresql_flexible_server_database.prod.id
  lock_level = "CanNotDelete"
  notes      = "Protect production database from accidental deletion"
}

resource "azurerm_management_lock" "db_preprod" {
  name       = "no-delete-db-preprod"
  scope      = azurerm_postgresql_flexible_server_database.preprod.id
  lock_level = "CanNotDelete"
  notes      = "Protect preprod database from accidental deletion"
}
