# ── Une PR, locataire de l'environnement preview ─────────────────
#
# Les PR sont le cas particulier de l'architecture. Un environnement complet par
# PR reviendrait à un serveur PostgreSQL par PR, quand l'environnement `preview`
# en fournit déjà un qui ne sert qu'à ça. Une PR n'apporte donc que ce qui lui
# est propre : sa base, ses deux applications, ses enregistrements DNS.
#
# Ce qu'elle emprunte à l'environnement `preview` — réseau, serveur, Container
# Apps Environment, identité — est lu par data source sur des noms fixes, jamais
# par terraform_remote_state : un service principal ne lit que son propre state.
#
# C'est ce découpage qui tient le plafond de trois serveurs PostgreSQL (prod,
# preprod, preview) quel que soit le nombre de PR ouvertes simultanément.

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

  # La clé est passée en -backend-config="key=pr-<n>.tfstate" : chaque PR a son
  # state, donc pas de for_each, pas de -target, aucune interférence entre
  # previews. Le storage account l'est aussi — il est propre à la souscription
  # des environnements jetables.
  backend "azurerm" {
    use_oidc = true
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

# ── L'environnement preview, lu par nom fixe ─────────
#
# Tout vient maintenant du resource group de l'environnement preview, y compris
# le serveur PostgreSQL et l'identité : le resource group partagé ne porte plus
# ni l'un ni l'autre.

locals {
  preview_resource_group_name = "rg-${var.project}-preview"
}

data "azurerm_container_app_environment" "preview" {
  name                = "cae-${var.project}-preview"
  resource_group_name = local.preview_resource_group_name
}

data "azurerm_user_assigned_identity" "preview" {
  name                = "id-${var.project}-preview"
  resource_group_name = local.preview_resource_group_name
}

data "azurerm_postgresql_flexible_server" "preview" {
  name                = "psql-${var.project}-preview"
  resource_group_name = local.preview_resource_group_name
}

locals {
  # Un certificat d'environnement n'a pas de data source azurerm : son ID se
  # dérive de celui du CAE (le certificat lui-même est posé par la racine
  # `environment`, par référence au secret Key Vault).
  wildcard_certificate_id = "${data.azurerm_container_app_environment.preview.id}/certificates/wildcard-houseflow-cloud"
}
