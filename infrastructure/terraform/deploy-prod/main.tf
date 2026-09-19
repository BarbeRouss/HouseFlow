terraform {
  required_version = ">= 1.5"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
  }

  backend "azurerm" {
    resource_group_name  = "rg-houseflow-shared"
    storage_account_name = "sthouseflowtfstate"
    container_name       = "tfstate-prod"
    key                  = "deploy-prod.tfstate"
    use_oidc             = true
  }
}

provider "azurerm" {
  features {}
  use_oidc                        = true
  subscription_id                 = var.subscription_id
  resource_provider_registrations = "none"
}

# ── Ressources préexistantes, lues par nom fixe ──────
#
# Aucun terraform_remote_state : les stacks amont (`shared`, `env-prod`)
# exposent leur contrat par des noms stables, pas par des outputs.

data "azurerm_container_app_environment" "prod" {
  name                = "cae-${var.project}-prod"
  resource_group_name = local.resource_group_name
}

# Les identités managées vivent dans le RG partagé : c'est `shared` qui leur
# pose leur RBAC, sans dépendre des stacks d'environnement.
data "azurerm_user_assigned_identity" "prod" {
  name                = "id-${var.project}-prod"
  resource_group_name = local.shared_resource_group_name
}

data "azurerm_postgresql_flexible_server" "shared" {
  name                = "psql-${var.project}"
  resource_group_name = local.shared_resource_group_name
}

locals {
  resource_group_name        = "rg-${var.project}-prod"
  shared_resource_group_name = "rg-${var.project}-shared"

  ghcr_owner     = lower(var.ghcr_username)
  api_image      = "ghcr.io/${local.ghcr_owner}/houseflow-api"
  frontend_image = "ghcr.io/${local.ghcr_owner}/houseflow-frontend"

  # Un certificat d'environnement n'a pas de data source azurerm : son ID se
  # dérive de celui du CAE (le certificat lui-même est posé par env-prod, par
  # référence au secret Key Vault).
  wildcard_certificate_id = "${data.azurerm_container_app_environment.prod.id}/certificates/wildcard-houseflow-cloud"
}
