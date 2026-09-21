# ── Le serveur PostgreSQL de l'environnement ─────────
#
# C'est le déplacement qui justifie tout le lot : le serveur appartient
# désormais à l'environnement. Une montée de version majeure, un changement de
# SKU, de stockage ou de paramètre serveur s'éprouve sur une instance jetable
# et n'a plus la production pour seul terrain d'essai.

resource "azurerm_postgresql_flexible_server" "env" {
  name                          = "psql-${var.project}-${var.name}"
  location                      = azurerm_resource_group.env.location
  resource_group_name           = azurerm_resource_group.env.name
  version                       = var.pg_version
  sku_name                      = var.pg_sku
  storage_mb                    = var.pg_storage_mb
  backup_retention_days         = var.pg_backup_retention_days
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

  tags = local.tags

  depends_on = [azurerm_private_dns_zone_virtual_network_link.postgres]

  lifecycle {
    # La zone de disponibilité est choisie par Azure au premier apply ;
    # la relire provoquerait un remplacement du serveur.
    ignore_changes = [zone]
  }
}

# ── Administrateurs Entra ────────────────────────────
#
# L'identité de l'environnement est administratrice de son propre serveur, et
# d'aucun autre. C'est ce qui fait disparaître le job `dbtools roles` : il
# n'existe plus d'identité privilégiée chargée de créer les rôles des autres
# environnements, puisqu'aucun environnement ne partage plus de serveur.

resource "azurerm_postgresql_flexible_server_active_directory_administrator" "user" {
  server_name         = azurerm_postgresql_flexible_server.env.name
  resource_group_name = azurerm_resource_group.env.name
  tenant_id           = data.azurerm_client_config.current.tenant_id
  object_id           = var.entra_admin_object_id
  principal_name      = var.entra_admin_name
  principal_type      = "User"
}

resource "azurerm_postgresql_flexible_server_active_directory_administrator" "env" {
  server_name         = azurerm_postgresql_flexible_server.env.name
  resource_group_name = azurerm_resource_group.env.name
  tenant_id           = data.azurerm_client_config.current.tenant_id
  object_id           = azurerm_user_assigned_identity.env.principal_id
  principal_name      = azurerm_user_assigned_identity.env.name
  principal_type      = "ServicePrincipal"
}

# ── Base applicative ─────────────────────────────────
#
# Créée par Terraform, et non plus en SQL par un job d'administration : chaque
# environnement possède son serveur, donc son identité en est administratrice
# et il n'y a plus de base à créer sur le serveur d'autrui.

resource "azurerm_postgresql_flexible_server_database" "env" {
  name      = "${var.project}_${replace(var.name, "-", "_")}"
  server_id = azurerm_postgresql_flexible_server.env.id
  charset   = "UTF8"
  collation = "en_US.utf8"
}

locals {
  database_name = azurerm_postgresql_flexible_server_database.env.name

  db_connection_string = join(";", [
    "Host=${azurerm_postgresql_flexible_server.env.fqdn}",
    "Port=5432",
    "Database=${local.database_name}",
    "Username=${azurerm_user_assigned_identity.env.name}",
    "SSL Mode=Require",
    "Trust Server Certificate=true",
  ])
}
