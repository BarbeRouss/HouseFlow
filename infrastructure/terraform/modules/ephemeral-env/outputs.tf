output "api_url" {
  description = "Ephemeral API URL"
  value       = "https://${azurerm_container_app.api.ingress[0].fqdn}"
}

output "frontend_url" {
  description = "Ephemeral frontend URL (Static Web App)"
  value       = "https://${azurerm_static_web_app.frontend.default_host_name}"
}

output "swa_api_key" {
  description = "Deployment token for uploading the Blazor WASM build to the Static Web App"
  value       = azurerm_static_web_app.frontend.api_key
  sensitive   = true
}
