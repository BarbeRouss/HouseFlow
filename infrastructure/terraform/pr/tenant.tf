# ── Ce que la PR apporte en propre ───────────────────
#
# La base `houseflow_pr_<n>` n'est pas créée ici : le job `dbtools init` (déployé
# par la racine `environment` sur l'instance preview, sous l'identité
# `id-houseflow-preview`) la crée en SQL et la supprime à la fermeture de la PR.
# Terraform ne fait que pointer dessus.

locals {
  ghcr_owner  = lower(var.ghcr_username)
  api_image   = "ghcr.io/${local.ghcr_owner}/houseflow-api"
  pr_database = "houseflow_pr_${var.pr_number}"
}

module "tenant" {
  source = "../modules/pr-tenant"

  pr_number                    = var.pr_number
  resource_group_name          = local.preview_resource_group_name
  container_app_environment_id = data.azurerm_container_app_environment.preview.id
  api_image                    = local.api_image
  image_tag                    = var.image_tag
  ghcr_username                = var.ghcr_username
  ghcr_pat                     = var.ghcr_pat
  jwt_key                      = var.jwt_key
  identity_id                  = data.azurerm_user_assigned_identity.preview.id
  identity_client_id           = data.azurerm_user_assigned_identity.preview.client_id

  container_app_environment_domain = data.azurerm_container_app_environment.preview.default_domain
  custom_domain_verification_id    = data.azurerm_container_app_environment.preview.custom_domain_verification_id
  wildcard_certificate_id          = local.wildcard_certificate_id

  db_connection_string = join(";", [
    "Host=${data.azurerm_postgresql_flexible_server.preview.fqdn}",
    "Port=5432",
    "Database=${local.pr_database}",
    "Username=${data.azurerm_user_assigned_identity.preview.name}",
    "SSL Mode=Require",
    "Trust Server Certificate=true",
  ])
}
