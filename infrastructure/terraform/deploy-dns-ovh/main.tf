# ── DNS OVH pour houseflow.cloud ──────────────────────
#
# Credentials OVH (OVH_ENDPOINT, OVH_APPLICATION_KEY, OVH_APPLICATION_SECRET,
# OVH_CONSUMER_KEY) lus depuis l'environnement par le provider ovh/ovh — ne
# jamais les passer en variable Terraform (fuite possible dans le plan/state).
# Toujours exécuté en CI (voir .github/workflows/infra.yml), jamais en
# session interactive.
#
# N'importe jamais/ne déclare jamais l'enregistrement racine ("") de la
# zone : la redirection houseflow.cloud -> www.houseflow.cloud est une
# config OVH statique, hors Terraform (voir specs/architecture.md).

terraform {
  required_version = ">= 1.5"

  required_providers {
    ovh = {
      source  = "ovh/ovh"
      version = "~> 2.0"
    }
  }

  backend "azurerm" {
    resource_group_name  = "rg-houseflow"
    storage_account_name = "sthouseflowtfstate"
    container_name       = "tfstate"
    key                  = "dns-ovh-prod.tfstate"
    use_oidc             = true
  }
}

provider "ovh" {
  endpoint = "ovh-eu"
}

# ── Ce stack ne dépend que du state `main` ───────────
#
# Le FQDN par défaut d'une Container App à ingress externe est
# `<nom-app>.<default_domain de l'environnement>` : il est donc dérivable sans
# lire les states applicatifs. C'est ce qui permet d'appliquer le DNS AVANT
# les apps — Azure refuse un hostname custom tant que son TXT asuid.* n'est pas
# résolvable, donc l'ordre imposé est : main → DNS → deploy-*.
# L'ID de vérification est lui aussi une propriété de l'environnement,
# commune à prod, preprod et previews.

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
  main   = data.terraform_remote_state.main.outputs
  domain = local.main.container_app_environment_domain
  asuid  = "\"${local.main.custom_domain_verification_id}\""

  # Hôtes durables. Convention : un seul label sous houseflow.cloud (le wildcard
  # *.houseflow.cloud ne couvre qu'un niveau), donc `api-preprod`, pas `api.preprod`.
  hosts = {
    "www"         = "ca-frontend-prod"
    "api"         = "ca-api-prod"
    "preprod"     = "ca-frontend-preprod"
    "api-preprod" = "ca-api-preprod"
    # Ancien nom, conservé le temps de basculer deploy-preprod sur `api-preprod`.
    "api.preprod" = "ca-api-preprod"
  }

  records = concat(
    [for host, app in local.hosts : {
      subdomain = host
      fieldtype = "CNAME"
      target    = "${app}.${local.domain}."
    }],
    [for host, app in local.hosts : {
      subdomain = "asuid.${host}"
      fieldtype = "TXT"
      target    = local.asuid
    }],
  )
}

# ── houseflow.cloud ───────────────────────────────────

module "houseflow_cloud" {
  source    = "../modules/ovh-dns-zone"
  zone_name = "houseflow.cloud"
  records   = local.records
}
