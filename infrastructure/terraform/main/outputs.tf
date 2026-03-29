locals {
  ghcr_owner = lower(var.ghcr_username)
}

# ── PostgreSQL ────────────────────────────────────────

output "postgresql_fqdn" {
  description = "PostgreSQL Flexible Server FQDN"
  value       = azurerm_postgresql_flexible_server.main.fqdn
}

# ── Container Apps Environment ────────────────────────

output "container_app_environment_id" {
  description = "Container Apps Environment ID (used by ephemeral envs)"
  value       = azurerm_container_app_environment.main.id
}

output "container_app_environment_domain" {
  description = "Default domain of the Container Apps Environment"
  value       = azurerm_container_app_environment.main.default_domain
}

# ── Bastion ──────────────────────────────────────────

output "bastion_fqdn" {
  description = "Bastion SSH host for DB tunnel (port 2222)"
  value       = azurerm_container_app.bastion.ingress[0].fqdn
}

# ── Shared resources for ephemeral environments ──────

output "resource_group_name" {
  description = "Resource group name"
  value       = data.azurerm_resource_group.main.name
}

output "pg_server_id" {
  description = "PostgreSQL Flexible Server ID"
  value       = azurerm_postgresql_flexible_server.main.id
}

output "pg_host" {
  description = "PostgreSQL Flexible Server FQDN (for connection strings)"
  value       = azurerm_postgresql_flexible_server.main.fqdn
}

output "identity_id" {
  description = "User-assigned managed identity ID"
  value       = azurerm_user_assigned_identity.main.id
}

output "identity_client_id" {
  description = "Client ID of the managed identity"
  value       = azurerm_user_assigned_identity.main.client_id
}

output "identity_name" {
  description = "Name of the managed identity (used as PG username)"
  value       = azurerm_user_assigned_identity.main.name
}

output "ghcr_owner" {
  description = "Lowercased GHCR owner for image paths"
  value       = local.ghcr_owner
}

# ── Database connection strings (used by deploy-prod/preprod) ─

output "pg_connection_prod" {
  description = "Production PostgreSQL connection string (passwordless)"
  value       = local.pg_connection_prod
  sensitive   = true
}

output "pg_connection_preprod" {
  description = "Preprod PostgreSQL connection string (passwordless)"
  value       = local.pg_connection_preprod
  sensitive   = true
}

output "pg_prod_db_name" {
  description = "Production database name"
  value       = azurerm_postgresql_flexible_server_database.prod.name
}

output "pg_preprod_db_name" {
  description = "Preprod database name"
  value       = azurerm_postgresql_flexible_server_database.preprod.name
}
