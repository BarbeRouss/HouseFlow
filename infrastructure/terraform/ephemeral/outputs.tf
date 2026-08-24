output "api_url" {
  description = "Ephemeral API URL"
  value       = module.pr_env.api_url
}

output "frontend_url" {
  description = "Ephemeral frontend URL (Static Web App)"
  value       = module.pr_env.frontend_url
}

output "swa_api_key" {
  description = "Static Web App deployment token for uploading the Blazor WASM build"
  value       = module.pr_env.swa_api_key
  sensitive   = true
}
