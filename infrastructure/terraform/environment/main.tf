# ── Un environnement HouseFlow complet et autonome ───────────────
#
# Cette racine est instanciée plusieurs fois, une par environnement, et ne
# diffère d'une instance à l'autre que par ses variables :
#
#   name = prod     expires_at = ""       → la production, permanente
#   name = pr-123   expires_at = <date>   → l'environnement d'une pull request
#
# La prod n'est pas un cas particulier du code : c'est l'instance dont
# l'échéance est vide, ce dont découlent le verrou et le réplica maintenu. Une
# PR fait donc tourner exactement le même apply que celui qui touchera la prod
# au merge — et c'est ce qui rend un changement d'infrastructure (version
# PostgreSQL, SKU, paramètres serveur, subnet) éprouvable dans la PR qui
# l'introduit, sans environnement de validation séparé.
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

  # Le storage account, la clé et le resource group sont passés en
  # -backend-config : le storage account et le resource group partagé portent
  # tous deux la souscription dans leur nom (`-prod` / `-ephemeral`), donc
  # diffèrent d'un `init` à l'autre bien que cette racine serve les deux
  # souscriptions ; la clé isole les instances (`environment-prod.tfstate`,
  # `environment-pr-42.tfstate`). Le conteneur, lui, s'appelle `tfstate` des
  # deux côtés.
  backend "azurerm" {
    container_name = "tfstate"
    use_oidc       = true
    # Authentification AAD sur le plan de données du storage : sans elle, le
    # backend passe par les clés du compte (listKeys), que le service principal
    # n'a pas — c'est le RBAC sur le conteneur qui doit trancher.
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

  # Une échéance vide marque un environnement permanent : pas de tag `ttl`, donc
  # invisible pour le reaper. C'est la seule protection qui ne dépende pas d'un
  # lock — le reaper ne détruit que ce qui porte une échéance dépassée.
  is_permanent = var.expires_at == ""

  # Ces deux-là ne sont pas des réglages mais des conséquences de la permanence.
  # En faire des variables rendait représentable l'environnement éphémère ET
  # verrouillé — une base promise à la destruction que le reaper ne peut pas
  # détruire, soit précisément la fuite que tout ce design écarte. Un état qu'on
  # ne peut pas écrire est un état qu'on ne peut pas atteindre par erreur.
  db_lock_enabled  = local.is_permanent
  api_min_replicas = local.is_permanent ? 1 : 0

  # Les deux ressources partagées, lues par nom fixe dans le resource group
  # permanent de CETTE souscription. Comme pour `dumps_identity`
  # (`dbtools.tf`), déduites de la permanence plutôt que passées en variable :
  # un environnement jetable n'a besoin de rien connaître d'autre que sa
  # propre situation pour trouver le bon nom.
  shared_resource_group_name = local.is_permanent ? "rg-houseflow-shared-prod" : "rg-houseflow-shared-ephemeral"
  certificate_identity_name  = local.is_permanent ? "id-houseflow-cert-prod" : "id-houseflow-cert-ephemeral"

  # Les sous-domaines posés dans la zone OVH, portés par le resource group pour
  # que sa destruction n'ait besoin de rien d'autre que lui-même. C'est ce qui
  # permet au reaper — qui n'a ni Terraform ni state, et c'est sa raison d'être —
  # de retirer le DNS aussi bien que le cleanup d'une PR : les deux appellent
  # `scripts/ci/destroy-environment.sh`, qui lit ce tag. Déduire les noms d'une
  # convention aurait marché aussi, jusqu'au jour où `dns.tf` change d'hôtes sans
  # que personne ne pense au script.
  #
  # Reconstruits depuis les variables, et non depuis `local.api_records` /
  # `local.web_records` : ceux-là portent les cibles, donc dépendent du CAE et de
  # la Static Web App, qui dépendent du resource group — le tag refermerait le
  # cycle. Les sous-domaines, eux, sont connus avant tout déploiement. Les deux
  # listes restent gouvernées par les mêmes `deploy_api` / `deploy_web`, donc un
  # hôte absent du DNS l'est aussi du tag.
  dns_hosts = join(",", concat(
    local.deploy_api ? [var.api_host, "asuid.${var.api_host}"] : [],
    local.deploy_web ? [var.frontend_host] : [],
  ))

  tags = merge(
    {
      project     = var.project
      environment = var.name
      managed-by  = "terraform"
    },
    local.dns_hosts == "" ? {} : { dns-hosts = local.dns_hosts },
    local.is_permanent ? {} : { ttl = var.expires_at },
  )
}

resource "azurerm_resource_group" "env" {
  name     = local.resource_group_name
  location = var.location
  tags     = local.tags
}
