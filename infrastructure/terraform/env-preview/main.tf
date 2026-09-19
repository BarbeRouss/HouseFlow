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
    container_name       = "tfstate-nonprod"
    key                  = "env-preview.tfstate"
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

  env                 = "preview"
  resource_group_name = "rg-houseflow-preview"
  address_space       = "10.2.0.0/16"
  cae_subnet_prefix   = "10.2.0.0/23"

  bastion_enabled = false
  rg_lock_enabled = false

  # Création et suppression des bases houseflow_pr_<n> par pr-preview.yml.
  dbtools_jobs  = ["init"]
  dbtools_image = "ghcr.io/barberouss/houseflow-dbtools:${var.dbtools_image_tag}"
  ghcr_pat      = var.ghcr_pat
}
