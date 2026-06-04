output "api_prod_url" {
  description = "Production API URL"
  value       = "https://${azurerm_container_app.api_prod.ingress[0].fqdn}"
}

output "frontend_prod_url" {
  description = "Production frontend URL"
  value       = "https://${azurerm_container_app.frontend_prod.ingress[0].fqdn}"
}

output "domain_verification_id" {
  description = "TXT record value for asuid.<domain> DNS records"
  value       = data.azurerm_container_app_environment.main.custom_domain_verification_id
}
