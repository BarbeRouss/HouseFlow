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
    key                  = "main.tfstate"
    use_oidc             = true
  }
}

provider "azurerm" {
  features {}
  use_oidc                       = true
  subscription_id                = var.subscription_id
  resource_provider_registrations = "none"
}
