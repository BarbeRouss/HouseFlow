terraform {
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
    # Enregistrements DNS de la preview (module ovh-dns-zone) — credentials
    # lus depuis l'environnement par le provider, configuré à la racine.
    ovh = {
      source  = "ovh/ovh"
      version = "~> 2.0"
    }
    time = {
      source  = "hashicorp/time"
      version = "~> 0.12"
    }
  }
}
