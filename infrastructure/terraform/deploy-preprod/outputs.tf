output "api_preprod_url" {
  description = "Preprod API URL"
  value       = "https://${azurerm_container_app.api_preprod.ingress[0].fqdn}"
}

output "frontend_preprod_url" {
  description = "Preprod frontend URL"
  value       = "https://${azurerm_container_app.frontend_preprod.ingress[0].fqdn}"
}
