# Ces sorties documentent le stack ; les autres stacks ne les lisent pas —
# ils passent par des data sources sur les noms fixes (aucun remote_state).

output "resource_group_name" {
  description = "Resource group partagé"
  value       = data.azurerm_resource_group.shared.name
}

output "vnet_id" {
  description = "ID du VNet partagé (cible des peerings)"
  value       = azurerm_virtual_network.shared.id
}

output "vnet_name" {
  description = "Nom du VNet partagé"
  value       = azurerm_virtual_network.shared.name
}

output "db_subnet_id" {
  description = "ID du subnet délégué à PostgreSQL"
  value       = azurerm_subnet.db.id
}

output "private_dns_zone_name" {
  description = "Zone DNS privée du serveur PostgreSQL"
  value       = azurerm_private_dns_zone.postgres.name
}

output "postgresql_id" {
  description = "ID du serveur PostgreSQL"
  value       = azurerm_postgresql_flexible_server.main.id
}

output "postgresql_fqdn" {
  description = "FQDN privé du serveur PostgreSQL"
  value       = azurerm_postgresql_flexible_server.main.fqdn
}

output "key_vault_id" {
  description = "ID du Key Vault"
  value       = azurerm_key_vault.main.id
}

output "key_vault_name" {
  description = "Nom du Key Vault"
  value       = azurerm_key_vault.main.name
}

output "key_vault_uri" {
  description = "URI du Key Vault"
  value       = azurerm_key_vault.main.vault_uri
}

output "certificate_name" {
  description = "Nom du certificat wildcard dans le Key Vault"
  value       = local.wildcard_certificate_name
}

output "db_dumps_container_name" {
  description = "Conteneur blob des dumps de base"
  value       = azurerm_storage_container.db_dumps.name
}

output "identity_ids" {
  description = "IDs des identités managées, par environnement"
  value       = { for env, id in azurerm_user_assigned_identity.env : env => id.id }
}

output "identity_client_ids" {
  description = "Client IDs des identités managées, par environnement"
  value       = { for env, id in azurerm_user_assigned_identity.env : env => id.client_id }
}

output "identity_principal_ids" {
  description = "Principal IDs des identités managées, par environnement"
  value       = { for env, id in azurerm_user_assigned_identity.env : env => id.principal_id }
}

output "identity_names" {
  description = "Noms des identités managées (utilisés comme rôles PostgreSQL), par environnement"
  value       = { for env, id in azurerm_user_assigned_identity.env : env => id.name }
}
