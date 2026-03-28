# ── Ephemeral PR environments ────────────────────────
# Isolated state: only Container Apps + PR databases live here.
# Shared resources (postgres server, CAE, identity) are read
# from the main state via terraform_remote_state.

locals {
  ghcr_owner     = local.main.ghcr_owner
  api_image      = "ghcr.io/${local.ghcr_owner}/houseflow-api"
  frontend_image = "ghcr.io/${local.ghcr_owner}/houseflow-frontend"
}

resource "azurerm_postgresql_flexible_server_database" "pr" {
  for_each  = var.pr_envs
  name      = "houseflow_pr_${each.key}"
  server_id = local.main.pg_server_id
  charset   = "UTF8"
  collation = "en_US.utf8"
}

module "pr_env" {
  source   = "../modules/ephemeral-env"
  for_each = var.pr_envs

  pr_number                    = tonumber(each.key)
  resource_group_name          = local.main.resource_group_name
  container_app_environment_id = local.main.container_app_environment_id
  api_image                    = local.api_image
  frontend_image               = local.frontend_image
  image_tag                    = each.value.image_tag
  ghcr_username                = var.ghcr_username
  ghcr_pat                     = var.ghcr_pat
  jwt_key                      = var.jwt_key
  identity_id                  = local.main.identity_id
  identity_client_id           = local.main.identity_client_id
  environment_default_domain   = local.main.container_app_environment_domain

  db_connection_string = join(";", [
    "Host=${local.main.pg_host}",
    "Port=5432",
    "Database=${azurerm_postgresql_flexible_server_database.pr[each.key].name}",
    "Username=${local.main.identity_name}",
    "SSL Mode=Require",
    "Trust Server Certificate=true",
  ])
}
