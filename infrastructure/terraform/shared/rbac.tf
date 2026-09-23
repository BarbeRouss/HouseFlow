# Les deux attributions de rôle de l'infrastructure, chacune au scope le plus étroit
# possible. `sp-prod` ne peut poser que ces deux rôles-là : sa condition ABAC les nomme
# (docs/azure-setup-guide.md §4a).

# Au secret seul, et non au coffre : la référence Key Vault d'un CAE lit le PFX exporté du
# certificat, exposé comme secret de même nom.
resource "azurerm_role_assignment" "certificate_secret" {
  scope                = "${azurerm_key_vault.main.id}/secrets/${local.wildcard_certificate_name}"
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_user_assigned_identity.certificate.principal_id
}

# Au conteneur `db-dumps` seul : le même storage account porte les states Terraform.
resource "azurerm_role_assignment" "dumps_writer" {
  scope                = "${data.azurerm_storage_account.tfstate.id}/blobServices/default/containers/${azurerm_storage_container.db_dumps.name}"
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = azurerm_user_assigned_identity.dumps.principal_id
}
