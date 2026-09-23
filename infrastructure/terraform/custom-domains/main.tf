# ── Domaines personnalisés d'un environnement ────────
#
# Lie les noms posés par la racine `dns` aux ressources Azure de la racine
# `environment` : `api-<…>` sur la Container App de l'API (dans le CAE, avec le
# certificat wildcard), `<…>` / `www` sur la Static Web App (qui émet son propre
# certificat). Azure valide chaque liaison par résolution DNS publique : cette
# racine s'applique donc après `dns`, et attend la propagation avant de lier.
#
# Hors du verrou `ovh-dns-zone` : elle n'écrit rien dans la zone OVH, et c'est
# elle qui porte l'attente de propagation et la validation Azure, qui peuvent
# prendre plusieurs minutes (#238).
#
# Un state par instance : `custom-domains-prod.tfstate`, `custom-domains-pr-42.tfstate`.

terraform {
  required_version = ">= 1.5"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
    time = {
      source  = "hashicorp/time"
      version = "~> 0.12"
    }
  }

  # Même backend qu'`environment` : storage account, resource group et clé en
  # -backend-config — le resource group partagé porte la souscription dans son
  # nom (#230), donc ne peut plus être un littéral commun aux deux souscriptions.
  backend "azurerm" {
    container_name   = "tfstate"
    use_oidc         = true
    use_azuread_auth = true
  }
}

# La souscription vient d'ARM_SUBSCRIPTION_ID, que les workflows posent déjà pour
# le backend : la répéter en variable ne ferait qu'ouvrir un second chemin vers
# la même valeur.
provider "azurerm" {
  features {}
  use_oidc                        = true
  resource_provider_registrations = "none"
}

locals {
  # `environment` ne connaît que "prod" comme instance permanente ; toute autre
  # valeur de `name` est une PR. Même déduction que `local.is_permanent` dans
  # `environment/main.tf`, mais depuis `name` : cette racine ne reçoit pas
  # `expires_at`, elle n'en a pas l'usage.
  is_permanent               = var.name == "prod"
  shared_resource_group_name = local.is_permanent ? "rg-houseflow-shared-prod" : "rg-houseflow-shared-ephemeral"
}

data "terraform_remote_state" "environment" {
  backend = "azurerm"
  config = {
    resource_group_name  = local.shared_resource_group_name
    storage_account_name = var.tfstate_storage_account_name
    container_name       = "tfstate"
    key                  = "environment-${var.name}.tfstate"
    use_oidc             = true
    use_azuread_auth     = true
  }
}

locals {
  env      = data.terraform_remote_state.environment.outputs
  api      = local.env.api_custom_domain
  frontend = local.env.frontend_custom_domain
}

# Azure valide le TXT asuid.* et le CNAME par résolution DNS publique au moment
# de la liaison : on laisse la zone OVH se propager avant de tenter. Rejouée
# quand les enregistrements changent — cette racine ne voit pas l'apply de
# `dns`, seulement ce qu'`environment` lui demande de poser.
resource "time_sleep" "dns_propagation" {
  count = local.api != null || local.frontend != null ? 1 : 0

  create_duration = "60s"
  triggers = {
    records = jsonencode(local.env.dns_records)
  }
}

resource "azurerm_container_app_custom_domain" "api" {
  count = local.api != null ? 1 : 0

  name                                     = local.api.fqdn
  container_app_id                         = local.api.container_app_id
  certificate_binding_type                 = "SniEnabled"
  container_app_environment_certificate_id = local.api.certificate_id

  depends_on = [time_sleep.dns_propagation]
}

resource "azurerm_static_web_app_custom_domain" "frontend" {
  count = local.frontend != null ? 1 : 0

  static_web_app_id = local.frontend.static_web_app_id
  domain_name       = local.frontend.fqdn
  validation_type   = "cname-delegation"

  depends_on = [time_sleep.dns_propagation]
}
