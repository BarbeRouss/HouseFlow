# ── Ephemeral PR environment ─────────────────────────
# Each PR gets its own Terraform state (ephemeral-pr-{N}.tfstate),
# so there is no for_each, no -target, and no cross-PR interference.

locals {
  ghcr_owner     = local.main.ghcr_owner
  api_image      = "ghcr.io/${local.ghcr_owner}/houseflow-api"
  frontend_image = "ghcr.io/${local.ghcr_owner}/houseflow-frontend"
}

resource "azurerm_postgresql_flexible_server_database" "pr" {
  name      = "houseflow_pr_${var.pr_number}"
  server_id = local.main.pg_server_id
  charset   = "UTF8"
  collation = "en_US.utf8"
}

module "pr_env" {
  source = "../modules/ephemeral-env"

  pr_number                    = var.pr_number
  resource_group_name          = local.main.resource_group_name
  container_app_environment_id = local.main.container_app_environment_id
  api_image                    = local.api_image
  frontend_image               = local.frontend_image
  image_tag                    = var.image_tag
  ghcr_username                = var.ghcr_username
  ghcr_pat                     = var.ghcr_pat
  jwt_key                      = var.jwt_key
  identity_id                  = local.main.identity_id
  identity_client_id           = local.main.identity_client_id
  environment_default_domain   = local.main.container_app_environment_domain

  db_connection_string = join(";", [
    "Host=${local.main.pg_host}",
    "Port=5432",
    "Database=${azurerm_postgresql_flexible_server_database.pr.name}",
    "Username=${local.main.identity_name}",
    "SSL Mode=Require",
    "Trust Server Certificate=true",
  ])
}
