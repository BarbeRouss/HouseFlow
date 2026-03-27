# ── Ephemeral PR environments ────────────────────────
# These are created/destroyed by the pr-preview.yml workflow.
# The variable pr_envs is empty by default — the workflow passes
# the PR number to create an environment.

variable "pr_envs" {
  description = "Map of PR numbers to deploy as ephemeral environments"
  type        = map(object({ image_tag = string }))
  default     = {}
}

resource "azurerm_postgresql_flexible_server_database" "pr" {
  for_each  = var.pr_envs
  name      = "${var.project}_pr_${each.key}"
  server_id = azurerm_postgresql_flexible_server.main.id
  charset   = "UTF8"
  collation = "en_US.utf8"
}

module "pr_env" {
  source   = "./modules/ephemeral-env"
  for_each = var.pr_envs

  pr_number                    = tonumber(each.key)
  resource_group_name          = data.azurerm_resource_group.main.name
  container_app_environment_id = azurerm_container_app_environment.main.id
  api_image                    = local.api_image
  frontend_image               = local.frontend_image
  image_tag                    = each.value.image_tag
  ghcr_username                = var.ghcr_username
  ghcr_pat                     = var.ghcr_pat
  jwt_key                      = var.jwt_key
  identity_id                  = azurerm_user_assigned_identity.main.id
  identity_client_id           = azurerm_user_assigned_identity.main.client_id

  db_connection_string = join(";", [
    "Host=${local.pg_host}",
    "Port=5432",
    "Database=${azurerm_postgresql_flexible_server_database.pr[each.key].name}",
    "Username=${azurerm_user_assigned_identity.main.name}",
    "SSL Mode=Require",
    "Trust Server Certificate=true",
  ])
}

output "pr_env_urls" {
  description = "URLs for ephemeral PR environments"
  value = {
    for k, v in module.pr_env : k => {
      api_url      = v.api_url
      frontend_url = v.frontend_url
    }
  }
}
