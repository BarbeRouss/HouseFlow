output "resource_group_name" {
  description = "Resource group de l'environnement — cible du reaper"
  value       = azurerm_resource_group.env.name
}

output "expires_at" {
  description = "Échéance portée par le tag ttl, vide sur un environnement permanent"
  value       = var.expires_at
}

output "container_app_environment_id" {
  description = "CAE de l'environnement"
  value       = azurerm_container_app_environment.env.id
}

output "container_app_environment_name" {
  value = azurerm_container_app_environment.env.name
}

output "postgresql_fqdn" {
  description = "FQDN privé du serveur, résolvable depuis le VNet de l'environnement"
  value       = azurerm_postgresql_flexible_server.env.fqdn
}

output "postgresql_server_name" {
  value = azurerm_postgresql_flexible_server.env.name
}

output "identity_name" {
  description = "Identité de l'environnement, qui est aussi le nom de son rôle PostgreSQL"
  value       = azurerm_user_assigned_identity.env.name
}

output "identity_client_id" {
  value = azurerm_user_assigned_identity.env.client_id
}

output "api_url" {
  value = local.deploy_api ? "https://${local.api_fqdn}" : null
}

output "frontend_url" {
  value = local.deploy_web ? "https://${local.frontend_fqdn}" : null
}

output "static_web_app_api_key" {
  description = "Jeton de déploiement de la Static Web App — le pipeline téléverse le wwwroot compilé avec"
  value       = local.deploy_web ? azurerm_static_web_app.frontend[0].api_key : null
  sensitive   = true
}
