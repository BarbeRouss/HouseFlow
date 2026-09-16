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
    key                  = "dns-ovh.tfstate"
    use_oidc             = true
  }
}

provider "ovh" {
  endpoint = "ovh-eu"
}

# ── Read prod Container Apps FQDNs + verification ID ──

data "terraform_remote_state" "deploy_prod" {
  backend = "azurerm"
  config = {
    resource_group_name  = "rg-houseflow"
    storage_account_name = "sthouseflowtfstate"
    container_name       = "tfstate"
    key                  = "deploy-prod.tfstate"
    use_oidc             = true
  }
}

locals {
  deploy_prod   = data.terraform_remote_state.deploy_prod.outputs
  api_fqdn      = trimprefix(local.deploy_prod.api_prod_url, "https://")
  frontend_fqdn = trimprefix(local.deploy_prod.frontend_prod_url, "https://")
}

# ── houseflow.cloud (prod) ────────────────────────────

module "houseflow_cloud" {
  source    = "../modules/ovh-dns-zone"
  zone_name = "houseflow.cloud"

  records = [
    {
      subdomain = "www"
      fieldtype = "CNAME"
      target    = "${local.frontend_fqdn}."
    },
    {
      subdomain = "api"
      fieldtype = "CNAME"
      target    = "${local.api_fqdn}."
    },
    {
      subdomain = "asuid.www"
      fieldtype = "TXT"
      target    = "\"${local.deploy_prod.domain_verification_id}\""
    },
    {
      subdomain = "asuid.api"
      fieldtype = "TXT"
      target    = "\"${local.deploy_prod.domain_verification_id}\""
    },
  ]
}
