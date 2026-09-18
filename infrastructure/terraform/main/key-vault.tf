# ── Key Vault : source de vérité du certificat wildcard ──────────
#
# Le certificat *.houseflow.cloud est émis par .github/workflows/certificate.yml
# (Let's Encrypt, validation DNS-01 contre la zone OVH), importé ici, puis poussé
# sur le Container Apps Environment sous le nom `local.wildcard_certificate_name`.
# Key Vault est la copie durable : un environnement recréé à froid se
# re-provisionne depuis le certificat stocké, sans ré-émission ni intervention.
#
# Mode "access policies" plutôt que RBAC : accorder un rôle exigerait
# Microsoft.Authorization/roleAssignments/write, que l'identité de déploiement
# n'a pas, et les access policies couvrent exactement le besoin.

locals {
  wildcard_certificate_name = "wildcard-houseflow-cloud"
}

resource "azurerm_key_vault" "main" {
  name                       = "kv-${var.project}"
  location                   = var.location
  resource_group_name        = data.azurerm_resource_group.main.name
  tenant_id                  = data.azurerm_client_config.current.tenant_id
  sku_name                   = "standard"
  soft_delete_retention_days = 7
  purge_protection_enabled   = false
  rbac_authorization_enabled = false
}

# Identité de déploiement (OIDC GitHub) : écrit et relit le certificat.
resource "azurerm_key_vault_access_policy" "deployer" {
  key_vault_id = azurerm_key_vault.main.id
  tenant_id    = data.azurerm_client_config.current.tenant_id
  object_id    = data.azurerm_client_config.current.object_id

  certificate_permissions = ["Get", "List", "Import", "Update", "Delete", "Purge", "Recover"]
  secret_permissions      = ["Get", "List"]
}

# Managed identity partagée des Container Apps : lecture seule.
resource "azurerm_key_vault_access_policy" "identity" {
  key_vault_id = azurerm_key_vault.main.id
  tenant_id    = data.azurerm_client_config.current.tenant_id
  object_id    = azurerm_user_assigned_identity.main.principal_id

  certificate_permissions = ["Get", "List"]
  secret_permissions      = ["Get", "List"]
}
