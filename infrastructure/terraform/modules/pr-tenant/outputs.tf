output "api_url" {
  description = "Ephemeral API URL (custom domain, wildcard certificate)"
  value       = "https://${local.api_domain}"
}

output "frontend_url" {
  description = "Ephemeral frontend URL (custom domain on the Static Web App)"
  value       = "https://${local.frontend_domain}"
}

output "swa_api_key" {
  description = "Deployment token for uploading the Blazor WASM build to the Static Web App"
  value       = azurerm_static_web_app.frontend.api_key
  sensitive   = true
}
