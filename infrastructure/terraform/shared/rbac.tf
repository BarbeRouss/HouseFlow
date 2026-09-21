# ── RBAC data-plane ──────────────────────────────────
#
# Une seule attribution, posée une fois : l'identité du certificat peut lire le
# secret du certificat wildcard, et rien d'autre. Portée au secret seul — la
# référence Key Vault d'un CAE lit le PFX exporté du certificat, exposé comme
# secret de même nom.
#
# C'est volontairement la seule attribution de rôle de toute l'infrastructure.
# Le rôle « HouseFlow Deployer » n'accorde pas `roleAssignments/write` : ce
# stack est le seul à pouvoir en créer, via le `Role Based Access Control
# Administrator` conditionné de `sp-prod` sur ce resource group. Tout design
# qui demanderait une attribution par environnement serait donc inapplicable
# aux environnements éphémères.

resource "azurerm_role_assignment" "certificate_secret" {
  scope                = "${azurerm_key_vault.main.id}/secrets/${local.wildcard_certificate_name}"
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_user_assigned_identity.certificate.principal_id
}

# Le conteneur `db-dumps` n'a plus d'attribution ici : les identités qui le
# liraient vivent maintenant dans les resource groups d'environnement, hors de
# portée de ce stack. C'est #199 (restauration d'un dump pseudonymisé) qui
# tranchera comment un environnement y accède — vraisemblablement par une
# identité partagée supplémentaire, sur le modèle de celle du certificat.
