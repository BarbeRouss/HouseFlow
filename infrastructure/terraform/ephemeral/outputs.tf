output "pr_env_urls" {
  description = "URLs for ephemeral PR environments"
  value = {
    for k, v in module.pr_env : k => {
      api_url      = v.api_url
      frontend_url = v.frontend_url
    }
  }
}
