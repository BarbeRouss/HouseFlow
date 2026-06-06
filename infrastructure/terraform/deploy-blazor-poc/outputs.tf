output "api_poc_url" {
  description = "Blazor POC API URL"
  value       = "https://${azurerm_container_app.api_poc.ingress[0].fqdn}"
}

output "swa_url" {
  description = "Blazor POC Static Web App URL"
  value       = "https://${azurerm_static_web_app.poc.default_host_name}"
}

output "swa_deploy_token" {
  description = "Static Web App deployment token (used by the publish step)"
  value       = azurerm_static_web_app.poc.api_key
  sensitive   = true
}
