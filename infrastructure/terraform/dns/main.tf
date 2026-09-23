# ── Enregistrements OVH d'un environnement ───────────
#
# La seule racine qui écrit dans la zone OVH partagée, donc la seule que le
# groupe de concurrence `ovh-dns-zone` sérialise — et elle ne fait que ça, pour
# que le verrou ne dure que le temps de quelques écritures (#238). Les
# enregistrements sont calculés par la racine `environment` (leurs cibles sont
# des attributs de son CAE et de sa Static Web App) et lus dans son state : cette
# racine s'applique donc après elle, et avant `domains`, qui lie les domaines
# côté Azure une fois le DNS propagé.
#
# Un state par instance, à côté de celui d'`environment` : `dns-prod.tfstate`,
# `dns-pr-42.tfstate`.

terraform {
  required_version = ">= 1.5"

  required_providers {
    ovh = {
      source  = "ovh/ovh"
      version = "~> 2.0"
    }
  }

  # Même backend qu'`environment` : storage account et clé en -backend-config.
  backend "azurerm" {
    resource_group_name = "rg-houseflow-shared"
    container_name      = "tfstate"
    use_oidc            = true
    use_azuread_auth    = true
  }
}

# Credentials OVH (OVH_APPLICATION_KEY, OVH_APPLICATION_SECRET, OVH_CONSUMER_KEY)
# lus depuis l'environnement — jamais en variable Terraform, qui les ferait
# fuiter dans le plan et le state.
provider "ovh" {
  endpoint = "ovh-eu"
}

data "terraform_remote_state" "environment" {
  backend = "azurerm"
  config = {
    resource_group_name  = "rg-houseflow-shared"
    storage_account_name = var.tfstate_storage_account_name
    container_name       = "tfstate"
    key                  = "environment-${var.name}.tfstate"
    use_oidc             = true
    use_azuread_auth     = true
  }
}

module "dns" {
  source    = "../modules/ovh-dns-zone"
  zone_name = data.terraform_remote_state.environment.outputs.dns_zone
  records   = data.terraform_remote_state.environment.outputs.dns_records
}
