terraform {
  required_version = ">= 1.5"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
  }

  backend "azurerm" {
    resource_group_name  = "rg-houseflow"
    storage_account_name = "sthouseflowtfstate"
    container_name       = "tfstate"
    key                  = "deploy-prod.tfstate"
    use_oidc             = true
  }
}

provider "azurerm" {
  features {}
  use_oidc                       = true
  subscription_id                = var.subscription_id
  resource_provider_registrations = "none"
}

# ── Read shared resources from main state ────────────

data "terraform_remote_state" "main" {
  backend = "azurerm"
  config = {
    resource_group_name  = "rg-houseflow"
    storage_account_name = "sthouseflowtfstate"
    container_name       = "tfstate"
    key                  = "main.tfstate"
    use_oidc             = true
  }
}

locals {
  main           = data.terraform_remote_state.main.outputs
  ghcr_owner     = local.main.ghcr_owner
  api_image      = "ghcr.io/${local.ghcr_owner}/houseflow-api"
  frontend_image = "ghcr.io/${local.ghcr_owner}/houseflow-frontend"
}
