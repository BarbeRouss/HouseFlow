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
    container_name       = "tfstate-shared"
    key                  = "shared.tfstate"
    use_oidc             = true
  }
}

provider "azurerm" {
  features {
    key_vault {
      # Les deux désactivés à dessein : ils appellent l'API des vaults supprimés,
      # de niveau souscription, hors de la portée (resource group) du rôle
      # "HouseFlow Deployer". Un vault détruit reste 7 jours en soft-delete :
      # le recréer sous le même nom dans ce délai demande une purge manuelle
      # (`az keyvault purge`) par un administrateur de la souscription.
      purge_soft_delete_on_destroy    = false
      recover_soft_deleted_key_vaults = false
    }
  }
  use_oidc                        = true
  subscription_id                 = var.subscription_id
  resource_provider_registrations = "none"
}

data "azurerm_client_config" "current" {}

# Créé au bootstrap, comme les trois resource groups d'environnement.
data "azurerm_resource_group" "shared" {
  name = "rg-houseflow-shared"
}
