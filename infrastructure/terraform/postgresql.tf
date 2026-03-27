resource "azurerm_postgresql_flexible_server" "main" {
  name                          = "psql-${var.project}"
  location                      = var.location
  resource_group_name           = data.azurerm_resource_group.main.name
  version                       = "16"
  administrator_login           = "houseflow"
  administrator_password        = var.pg_admin_password
  sku_name                      = var.pg_sku
  storage_mb                    = var.pg_storage_mb
  backup_retention_days         = 7
  geo_redundant_backup_enabled  = false
  public_network_access_enabled = false
  zone                          = "3"

  # VNet integration
  delegated_subnet_id = azurerm_subnet.db.id
  private_dns_zone_id = azurerm_private_dns_zone.postgres.id

  # Entra ID (Azure AD) authentication
  authentication {
    active_directory_auth_enabled = true
    password_auth_enabled         = true # Fallback for debug/migration
    tenant_id                     = data.azurerm_client_config.current.tenant_id
  }

  depends_on = [azurerm_private_dns_zone_virtual_network_link.postgres]

  lifecycle {
    ignore_changes = [zone]
  }
}

# ── Entra ID Admin: Managed Identity (for Container Apps) ──

resource "azurerm_postgresql_flexible_server_active_directory_administrator" "managed_identity" {
  server_name         = azurerm_postgresql_flexible_server.main.name
  resource_group_name = data.azurerm_resource_group.main.name
  tenant_id           = data.azurerm_client_config.current.tenant_id
  object_id           = azurerm_user_assigned_identity.main.principal_id
  principal_name      = azurerm_user_assigned_identity.main.name
  principal_type      = "ServicePrincipal"
}

# ── Entra ID Admin: User account (for debug via az login + psql) ──

resource "azurerm_postgresql_flexible_server_active_directory_administrator" "user" {
  server_name         = azurerm_postgresql_flexible_server.main.name
  resource_group_name = data.azurerm_resource_group.main.name
  tenant_id           = data.azurerm_client_config.current.tenant_id
  object_id           = var.entra_admin_object_id
  principal_name      = var.entra_admin_name
  principal_type      = "User"
}

# ── Databases ────────────────────────────────────────

resource "azurerm_postgresql_flexible_server_database" "prod" {
  name      = "${var.project}_prod"
  server_id = azurerm_postgresql_flexible_server.main.id
  charset   = "UTF8"
  collation = "en_US.utf8"
}

resource "azurerm_postgresql_flexible_server_database" "preprod" {
  name      = "${var.project}_preprod"
  server_id = azurerm_postgresql_flexible_server.main.id
  charset   = "UTF8"
  collation = "en_US.utf8"
}

# ── Connection strings (passwordless — no Password field) ──

locals {
  pg_host = azurerm_postgresql_flexible_server.main.fqdn

  pg_connection_prod = join(";", [
    "Host=${local.pg_host}",
    "Port=5432",
    "Database=${azurerm_postgresql_flexible_server_database.prod.name}",
    "Username=${azurerm_user_assigned_identity.main.name}",
    "SSL Mode=Require",
    "Trust Server Certificate=true",
  ])

  pg_connection_preprod = join(";", [
    "Host=${local.pg_host}",
    "Port=5432",
    "Database=${azurerm_postgresql_flexible_server_database.preprod.name}",
    "Username=${azurerm_user_assigned_identity.main.name}",
    "SSL Mode=Require",
    "Trust Server Certificate=true",
  ])
}
