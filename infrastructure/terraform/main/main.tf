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
  features {
    key_vault {
      # Un vault détruit reste 7 jours en soft-delete sous le même nom :
      # purger/récupérer automatiquement évite un "VaultAlreadyExists" au recreate.
      purge_soft_delete_on_destroy    = true
      recover_soft_deleted_key_vaults = true
    }
  }
  use_oidc                       = true
  subscription_id                = var.subscription_id
  resource_provider_registrations = "none"
}
