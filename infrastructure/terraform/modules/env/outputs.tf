output "resource_group_name" {
  description = "Resource group de l'environnement"
  value       = data.azurerm_resource_group.env.name
}

output "cae_id" {
  description = "ID du Container Apps Environment"
  value       = azurerm_container_app_environment.env.id
}

output "cae_name" {
  description = "Nom du Container Apps Environment"
  value       = azurerm_container_app_environment.env.name
}

output "cae_default_domain" {
  description = "Domaine par défaut du Container Apps Environment"
  value       = azurerm_container_app_environment.env.default_domain
}

output "cae_custom_domain_verification_id" {
  description = "Valeur des TXT asuid.<hôte> (preuve de propriété, commune à toutes les apps de l'environnement)"
  value       = azurerm_container_app_environment.env.custom_domain_verification_id
}

output "certificate_id" {
  description = "ID du certificat wildcard sur l'environnement (cible des bindings de domaine custom)"
  value       = azapi_resource.wildcard_certificate.id
}

output "identity_id" {
  description = "ID de l'identité managée de l'environnement"
  value       = data.azurerm_user_assigned_identity.env.id
}

output "identity_client_id" {
  description = "Client ID de l'identité managée"
  value       = data.azurerm_user_assigned_identity.env.client_id
}

output "identity_name" {
  description = "Nom de l'identité managée (utilisé comme rôle PostgreSQL)"
  value       = data.azurerm_user_assigned_identity.env.name
}

output "log_analytics_id" {
  description = "ID du workspace Log Analytics"
  value       = azurerm_log_analytics_workspace.env.id
}

output "bastion_fqdn" {
  description = "Hôte SSH du bastion (port 2222), null si le bastion est désactivé"
  value       = var.bastion_enabled ? azurerm_container_app.bastion[0].ingress[0].fqdn : null
}
