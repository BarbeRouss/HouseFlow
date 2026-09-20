terraform {
  required_version = ">= 1.5"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
    azapi = {
      source  = "Azure/azapi"
      version = "~> 2.0"
    }
  }

  backend "azurerm" {
    resource_group_name  = "rg-houseflow-shared"
    storage_account_name = "sthouseflowtfstate"
    container_name       = "tfstate-prod"
    key                  = "env-prod.tfstate"
    use_oidc             = true
  }
}

provider "azurerm" {
  features {}
  use_oidc                        = true
  subscription_id                 = var.subscription_id
  resource_provider_registrations = "none"
}

provider "azapi" {
  use_oidc        = true
  subscription_id = var.subscription_id
}

module "env" {
  source = "../modules/env"

  env                 = "prod"
  resource_group_name = "rg-houseflow-prod"

  bastion_enabled        = true
  bastion_ssh_public_key = var.bastion_ssh_public_key
  rg_lock_enabled        = true

  # id-houseflow-prod est la seule identité admin du serveur : c'est depuis ce
  # CAE que sont créés les rôles des autres environnements.
  dbtools_jobs  = ["roles"]
  dbtools_image = "ghcr.io/barberouss/houseflow-dbtools:${var.dbtools_image_tag}"
  ghcr_pat      = var.ghcr_pat
}
