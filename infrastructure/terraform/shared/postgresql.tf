resource "azurerm_postgresql_flexible_server" "main" {
  name                          = "psql-houseflow"
  location                      = var.location
  resource_group_name           = data.azurerm_resource_group.shared.name
  version                       = "16"
  sku_name                      = var.pg_sku
  storage_mb                    = var.pg_storage_mb
  backup_retention_days         = 7
  geo_redundant_backup_enabled  = false
  public_network_access_enabled = false

  delegated_subnet_id = azurerm_subnet.db.id
  private_dns_zone_id = azurerm_private_dns_zone.postgres.id

  # Entra ID uniquement — pas d'authentification par mot de passe.
  authentication {
    active_directory_auth_enabled = true
    password_auth_enabled         = false
    tenant_id                     = data.azurerm_client_config.current.tenant_id
  }

  depends_on = [azurerm_private_dns_zone_virtual_network_link.shared]

  lifecycle {
    # La zone de disponibilité est choisie par Azure au premier apply ;
    # la relire provoquerait un remplacement du serveur.
    ignore_changes = [zone]
  }
}

# ── Administrateurs Entra ────────────────────────────
# L'utilisateur (debug via az login + psql au travers du bastion) et l'identité
# de production, seule identité applicative admin : c'est elle qui crée les
# rôles des autres environnements (job `dbtools roles`).

resource "azurerm_postgresql_flexible_server_active_directory_administrator" "user" {
  server_name         = azurerm_postgresql_flexible_server.main.name
  resource_group_name = data.azurerm_resource_group.shared.name
  tenant_id           = data.azurerm_client_config.current.tenant_id
  object_id           = var.entra_admin_object_id
  principal_name      = var.entra_admin_name
  principal_type      = "User"
}

resource "azurerm_postgresql_flexible_server_active_directory_administrator" "prod_identity" {
  server_name         = azurerm_postgresql_flexible_server.main.name
  resource_group_name = data.azurerm_resource_group.shared.name
  tenant_id           = data.azurerm_client_config.current.tenant_id
  object_id           = azurerm_user_assigned_identity.env["prod"].principal_id
  principal_name      = azurerm_user_assigned_identity.env["prod"].name
  principal_type      = "ServicePrincipal"
}
