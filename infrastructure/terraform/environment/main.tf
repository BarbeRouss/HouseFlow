# ── Un environnement HouseFlow complet et autonome ───────────────
#
# Cette racine est instanciée plusieurs fois, une par environnement, et ne
# diffère d'une instance à l'autre que par ses variables :
#
#   name = prod     ttl_hours = 0   rg_lock_enabled = true    → la production
#   name = preprod  ttl_hours = 12                            → miroir jetable
#   name = preview  ttl_hours = 12                            → hôte des PR
#
# La prod n'est pas un cas particulier du code : c'est l'instance dont le TTL
# est nul et le resource group verrouillé. C'est ce qui rend le flux de
# déploiement de preprod réellement identique à celui de la prod — même apply,
# autre `name` — et donc capable de valider un changement d'infrastructure
# (version PostgreSQL, SKU, paramètres serveur, subnet) avant qu'il ne touche
# la production.
#
# Chaque instance possède son réseau, son serveur PostgreSQL, son Container
# Apps Environment et son identité. Rien n'est partagé entre deux instances
# sauf ce qui n'est ni du compute ni de la donnée : le certificat wildcard,
# la zone DNS et le storage des states.

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
    ovh = {
      source  = "ovh/ovh"
      version = "~> 2.0"
    }
    time = {
      source  = "hashicorp/time"
      version = "~> 0.12"
    }
  }

  # Entièrement passé en -backend-config, storage account compris : la
  # production et les environnements jetables vivent dans des souscriptions
  # distinctes, chacune avec son propre storage de states. Un state éphémère
  # n'a alors aucun chemin vers la souscription de production — ni pour le
  # lire, ni pour l'écrire, ni pour que le reaper s'y trompe de cible.
  #
  # La clé isole les instances entre elles : `environment-preview.tfstate`,
  # `environment-prod.tfstate`.
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

provider "azapi" {
  use_oidc        = true
  subscription_id = var.subscription_id
}

# Credentials OVH (OVH_ENDPOINT, OVH_APPLICATION_KEY, OVH_APPLICATION_SECRET,
# OVH_CONSUMER_KEY) lus depuis l'environnement — jamais en variable Terraform,
# qui les ferait fuiter dans le plan et le state.
provider "ovh" {
  endpoint = "ovh-eu"
}

data "azurerm_client_config" "current" {}

locals {
  resource_group_name = "rg-${var.project}-${var.name}"

  # Un TTL nul marque un environnement permanent : pas de tag `ttl`, donc
  # invisible pour le reaper. C'est la seule protection qui ne dépende pas
  # d'un lock — le reaper ne détruit que ce qui porte une échéance dépassée.
  is_permanent = var.ttl_hours == 0

  expires_at = local.is_permanent ? null : timeadd(time_static.created.rfc3339, "${var.ttl_hours}h")

  tags = merge(
    {
      project     = var.project
      environment = var.name
      managed-by  = "terraform"
    },
    local.is_permanent ? {} : { ttl = local.expires_at },
  )
}

# L'échéance est ancrée au premier apply et ne bouge plus : sans ça, chaque
# apply repousserait le TTL et un environnement redéployé régulièrement ne
# mourrait jamais.
resource "time_static" "created" {}

resource "azurerm_resource_group" "env" {
  name     = local.resource_group_name
  location = var.location
  tags     = local.tags
}
