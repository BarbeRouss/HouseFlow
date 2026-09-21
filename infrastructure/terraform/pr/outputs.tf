output "api_url" {
  description = "URL de l'API de la PR"
  value       = module.tenant.api_url
}

output "frontend_url" {
  description = "URL du frontend de la PR (Static Web App)"
  value       = module.tenant.frontend_url
}

output "swa_api_key" {
  description = "Static Web App deployment token for uploading the Blazor WASM build"
  value       = module.tenant.swa_api_key
  sensitive   = true
}
