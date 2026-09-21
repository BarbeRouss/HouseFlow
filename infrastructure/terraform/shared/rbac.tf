# Portée au secret seul, et non au coffre : la référence Key Vault d'un CAE lit
# le PFX exporté du certificat, exposé comme secret de même nom.

resource "azurerm_role_assignment" "certificate_secret" {
  scope                = "${azurerm_key_vault.main.id}/secrets/${local.wildcard_certificate_name}"
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_user_assigned_identity.certificate.principal_id
}

# Le conteneur `db-dumps` n'a pas d'attribution : les identités qui le liraient
# vivent dans les resource groups d'environnement, hors de portée de ce stack.
# C'est #199 qui tranchera comment un environnement y accède.
