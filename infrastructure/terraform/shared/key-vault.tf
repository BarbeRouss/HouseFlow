# ── Key Vault : source de vérité du certificat wildcard ──────────
#
# Le certificat *.houseflow.cloud est émis par le job `certificate` du pipeline
# (Let's Encrypt, validation DNS-01 contre la zone OVH) et importé ici. Chaque
# Container Apps Environment le référence ensuite directement depuis le Key
# Vault, sans version : un renouvellement ne redéploie rien.
#
# Mode RBAC : les droits data-plane des identités managées sont posés par ce
# stack (voir rbac.tf), au secret du certificat près. Les droits d'émission de
# l'identité de déploiement prod sont assignés au bootstrap.

locals {
  wildcard_certificate_name = "wildcard-houseflow-cloud"
}

resource "azurerm_key_vault" "main" {
  name                       = "kv-houseflow"
  location                   = var.location
  resource_group_name        = data.azurerm_resource_group.shared.name
  tenant_id                  = data.azurerm_client_config.current.tenant_id
  sku_name                   = "standard"
  soft_delete_retention_days = 7
  # Pas de purge protection : un vault détruit doit pouvoir être purgé
  # manuellement (`az keyvault purge`) pour être recréé sous le même nom.
  purge_protection_enabled   = false
  rbac_authorization_enabled = true
}
