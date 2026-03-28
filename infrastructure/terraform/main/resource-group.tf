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

resource "azurerm_management_lock" "api_prod" {
  name       = "no-delete-api-prod"
  scope      = azurerm_container_app.api_prod.id
  lock_level = "CanNotDelete"
  notes      = "Protect production API from accidental deletion"
}

resource "azurerm_management_lock" "frontend_prod" {
  name       = "no-delete-frontend-prod"
  scope      = azurerm_container_app.frontend_prod.id
  lock_level = "CanNotDelete"
  notes      = "Protect production frontend from accidental deletion"
}
