# Ces sorties documentent le stack ; les autres stacks ne les lisent pas —
# ils passent par des data sources sur les noms fixes (aucun remote_state).

output "resource_group_name" {
  description = "Resource group partagé"
  value       = data.azurerm_resource_group.shared.name
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

output "certificate_identity_name" {
  description = "Identité partagée habilitée à lire le secret du certificat — attachée par chaque CAE"
  value       = azurerm_user_assigned_identity.certificate.name
}

output "certificate_identity_id" {
  value = azurerm_user_assigned_identity.certificate.id
}

output "db_dumps_container_name" {
  description = "Conteneur blob des dumps de base"
  value       = azurerm_storage_container.db_dumps.name
}
