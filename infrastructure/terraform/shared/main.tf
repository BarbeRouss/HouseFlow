# ── Ce qui est réellement partagé ────────────────────
#
# Ni compute, ni donnée : seulement ce qu'un environnement ne peut pas
# posséder en propre.
#
#   le certificat wildcard   Let's Encrypt plafonne les certificats identiques
#                            à 5 par semaine — un environnement éphémère ne
#                            peut pas émettre le sien
#   le storage des states    il doit préexister à tout apply, y compris le sien
#   le conteneur db-dumps    passerelle entre la prod et les environnements
#                            qui rejouent ses données
#
# Le serveur PostgreSQL, le VNet et les identités d'environnement sont partis
# d'ici : ils appartiennent désormais à chaque environnement (racine
# `environment`). C'est tout l'objet du lot — ce qui reste ici ne bloque plus
# aucun changement d'infrastructure.
#
# Le resource group et le storage account sont créés au bootstrap, hors
# Terraform : le backend doit exister avant le premier apply.

terraform {
  required_version = ">= 1.5"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
  }

  # Passé en -backend-config comme celui de la racine `environment` : les noms
  # de storage account sont uniques au niveau mondial, donc chaque souscription
  # a le sien et aucun nom ne peut être codé en dur ici.
  backend "azurerm" {
    container_name = "tfstate"
    key            = "shared.tfstate"
    use_oidc       = true
    # Authentification AAD sur le plan de données du storage : sans elle, le
    # backend passe par les clés du compte (listKeys), que le service principal
    # n'a pas — c'est le RBAC sur le conteneur qui doit trancher.
    use_azuread_auth = true
  }
}

provider "azurerm" {
  features {
    key_vault {
      # Les deux désactivés à dessein : ils appellent l'API des vaults supprimés,
      # de niveau souscription, hors de la portée (resource group) du rôle
      # "HouseFlow Deployer". Un vault détruit reste 7 jours en soft-delete :
      # le recréer sous le même nom dans ce délai demande une purge manuelle
      # (`az keyvault purge`) par un administrateur de la souscription.
      purge_soft_delete_on_destroy    = false
      recover_soft_deleted_key_vaults = false
    }
  }
  use_oidc                        = true
  subscription_id                 = var.subscription_id
  resource_provider_registrations = "none"
}

data "azurerm_client_config" "current" {}

# Créé au bootstrap, comme les trois resource groups d'environnement.
data "azurerm_resource_group" "shared" {
  name = "rg-houseflow-shared"
}
