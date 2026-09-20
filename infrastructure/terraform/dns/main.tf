# ── DNS OVH pour houseflow.cloud ──────────────────────
#
# Credentials OVH (OVH_ENDPOINT, OVH_APPLICATION_KEY, OVH_APPLICATION_SECRET,
# OVH_CONSUMER_KEY) lus depuis l'environnement par le provider ovh/ovh — ne
# jamais les passer en variable Terraform (fuite possible dans le plan/state).
# Toujours exécuté en CI, jamais en session interactive.
#
# N'importe jamais/ne déclare jamais l'enregistrement racine ("") de la zone :
# la redirection houseflow.cloud -> www.houseflow.cloud est une config OVH
# statique, hors Terraform (voir specs/architecture.md).

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
  }

  backend "azurerm" {
    resource_group_name  = "rg-houseflow-shared"
    storage_account_name = "sthouseflowtfstate"
    container_name       = "tfstate-shared"
    key                  = "dns.tfstate"
    use_oidc             = true
    # Authentification AAD sur le plan de données du storage : sans elle, le
    # backend passe par les clés du compte (listKeys), que le rôle Shared Tenant
    # ne donne pas — c'est le RBAC par conteneur qui doit trancher.
    use_azuread_auth = true
  }
}

# azurerm ne sert qu'aux data sources ci-dessous : ce stack ne crée aucune
# ressource Azure.
provider "azurerm" {
  features {}
  use_oidc                        = true
  subscription_id                 = var.subscription_id
  resource_provider_registrations = "none"
}

provider "ovh" {
  endpoint = "ovh-eu"
}

# ── Les deux CAE durables, lus par nom fixe ──────────
#
# Le FQDN par défaut d'une Container App à ingress externe est
# `<nom-app>.<default_domain de l'environnement>` : il est dérivable sans lire
# les states applicatifs. C'est ce qui permet d'appliquer le DNS AVANT les
# apps — Azure refuse un hostname custom tant que son TXT asuid.* n'est pas
# résolvable, donc l'ordre imposé est : env-* → dns → deploy-*.
#
# Domaine par défaut et ID de vérification sont des propriétés de
# l'environnement : prod et preprod ayant chacun le leur, les valeurs
# diffèrent d'un hôte à l'autre.

data "azurerm_container_app_environment" "prod" {
  name                = "cae-${var.project}-prod"
  resource_group_name = "rg-${var.project}-prod"
}

data "azurerm_container_app_environment" "preprod" {
  name                = "cae-${var.project}-preprod"
  resource_group_name = "rg-${var.project}-preprod"
}

locals {
  environments = {
    prod = {
      domain = data.azurerm_container_app_environment.prod.default_domain
      asuid  = data.azurerm_container_app_environment.prod.custom_domain_verification_id
    }
    preprod = {
      domain = data.azurerm_container_app_environment.preprod.default_domain
      asuid  = data.azurerm_container_app_environment.preprod.custom_domain_verification_id
    }
  }

  # Hôtes durables. Convention : un seul label sous houseflow.cloud (le
  # wildcard *.houseflow.cloud ne couvre qu'un niveau), donc `api-preprod`,
  # pas `api.preprod`. Les hôtes de preview (pr-<n>) sont gérés par le stack
  # `ephemeral`.
  hosts = {
    "www"         = { app = "ca-frontend-prod", environment = "prod" }
    "api"         = { app = "ca-api-prod", environment = "prod" }
    "preprod"     = { app = "ca-frontend-preprod", environment = "preprod" }
    "api-preprod" = { app = "ca-api-preprod", environment = "preprod" }
  }

  records = concat(
    [for host, h in local.hosts : {
      subdomain = host
      fieldtype = "CNAME"
      target    = "${h.app}.${local.environments[h.environment].domain}."
    }],
    [for host, h in local.hosts : {
      subdomain = "asuid.${host}"
      fieldtype = "TXT"
      target    = "\"${local.environments[h.environment].asuid}\""
    }],
  )
}

# ── houseflow.cloud ───────────────────────────────────

module "houseflow_cloud" {
  source    = "../modules/ovh-dns-zone"
  zone_name = var.dns_zone
  records   = local.records
}
