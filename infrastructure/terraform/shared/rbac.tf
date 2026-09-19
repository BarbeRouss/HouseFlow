# ── RBAC data-plane des identités managées ───────────
# Portée minimale : le secret du certificat côté Key Vault, le conteneur des
# dumps côté storage. Seule la production écrit les dumps ; preprod et preview
# les relisent.

resource "azurerm_role_assignment" "certificate_secret" {
  for_each = azurerm_user_assigned_identity.env

  # Scope au secret seul : la référence Key Vault du CAE lit le PFX exporté du
  # certificat, exposé comme secret de même nom.
  scope                = "${azurerm_key_vault.main.id}/secrets/${local.wildcard_certificate_name}"
  role_definition_name = "Key Vault Secrets User"
  principal_id         = each.value.principal_id
}

resource "azurerm_role_assignment" "db_dumps" {
  for_each = {
    prod    = "Storage Blob Data Contributor"
    preprod = "Storage Blob Data Reader"
    preview = "Storage Blob Data Reader"
  }

  scope                = azurerm_storage_container.db_dumps.id
  role_definition_name = each.value
  principal_id         = azurerm_user_assigned_identity.env[each.key].principal_id
}
