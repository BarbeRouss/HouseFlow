output "api_url" {
  description = "Ephemeral API URL"
  value       = module.pr_env.api_url
}

output "frontend_url" {
  description = "Ephemeral frontend URL"
  value       = module.pr_env.frontend_url
}
