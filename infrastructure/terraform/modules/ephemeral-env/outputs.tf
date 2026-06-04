output "api_url" {
  description = "Ephemeral API URL"
  value       = "https://${azurerm_container_app.api.ingress[0].fqdn}"
}

output "frontend_url" {
  description = "Ephemeral frontend URL"
  value       = "https://${azurerm_container_app.frontend.ingress[0].fqdn}"
}
