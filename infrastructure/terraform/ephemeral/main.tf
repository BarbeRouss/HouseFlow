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

  # La clé est passée en -backend-config="key=ephemeral-pr-<n>.tfstate" :
  # chaque PR a son state, donc pas de for_each, pas de -target, aucune
  # interférence entre previews.
  backend "azurerm" {
    resource_group_name  = "rg-houseflow-shared"
    storage_account_name = "sthouseflowtfstate"
    container_name       = "tfstate-nonprod"
    use_oidc             = true
    # Authentification AAD sur le plan de données du storage : sans elle, le
    # backend passe par les clés du compte (listKeys), que le rôle Shared Tenant
    # ne donne pas — c'est le RBAC par conteneur qui doit trancher.
    use_azuread_auth = true
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

# ── Ressources préexistantes, lues par nom fixe ──────
#
# Aucun terraform_remote_state : les previews partagent le CAE `preview` et
# le serveur PostgreSQL du RG partagé, tous deux identifiés par leur nom.

data "azurerm_container_app_environment" "preview" {
  name                = "cae-${var.project}-preview"
  resource_group_name = local.resource_group_name
}

data "azurerm_user_assigned_identity" "preview" {
  name                = "id-${var.project}-preview"
  resource_group_name = local.shared_resource_group_name
}

data "azurerm_postgresql_flexible_server" "shared" {
  name                = "psql-${var.project}"
  resource_group_name = local.shared_resource_group_name
}

locals {
  resource_group_name        = "rg-${var.project}-preview"
  shared_resource_group_name = "rg-${var.project}-shared"

  # Un certificat d'environnement n'a pas de data source azurerm : son ID se
  # dérive de celui du CAE (le certificat lui-même est posé par env-preview,
  # par référence au secret Key Vault).
  wildcard_certificate_id = "${data.azurerm_container_app_environment.preview.id}/certificates/wildcard-houseflow-cloud"
}
