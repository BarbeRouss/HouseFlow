terraform {
  required_version = ">= 1.5"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
    ovh = {
      source  = "ovh/ovh"
      version = "~> 2.0"
    }
    time = {
      source  = "hashicorp/time"
      version = "~> 0.12"
    }
  }

  # State key is set dynamically via -backend-config="key=ephemeral-pr-{N}.tfstate"
  # Each PR gets its own isolated state file.
  backend "azurerm" {
    resource_group_name  = "rg-houseflow"
    storage_account_name = "sthouseflowtfstate"
    container_name       = "tfstate"
    use_oidc             = true
  }
}

provider "azurerm" {
  features {}
  use_oidc                        = true
  subscription_id                 = var.subscription_id
  resource_provider_registrations = "none"
}

# Credentials OVH (OVH_ENDPOINT, OVH_APPLICATION_KEY, OVH_APPLICATION_SECRET,
# OVH_CONSUMER_KEY) lus depuis l'environnement — jamais en variable Terraform.
provider "ovh" {
  endpoint = "ovh-eu"
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
  main = data.terraform_remote_state.main.outputs
}
