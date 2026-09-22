# Les deux seules identités de ce stack, et les seules de toute l'infrastructure à porter
# une attribution de rôle (rbac.tf). Les environnements les attachent au lieu d'utiliser
# la leur — c'est ce qui permet de créer un environnement jetable sans droit d'attribution.
#
# Le raisonnement est dans `infrastructure/rbac/README.md`, section « Les attributions de
# rôle de l'infrastructure ». Chacune a son homologue dans la souscription des
# environnements jetables, créé au bootstrap, avec des droits plus étroits.

# Attachée à chaque CAE pour résoudre la référence Key Vault du certificat wildcard.
resource "azurerm_user_assigned_identity" "certificate" {
  name                = "id-houseflow-cert"
  location            = var.location
  resource_group_name = data.azurerm_resource_group.shared.name
}

# Attachée au job `dbtools dump` de la prod, qui publie le dump pseudonymisé. Son nom dit
# son droit : son homologue jetable, `id-houseflow-dumps-reader`, ne fait que le lire.
resource "azurerm_user_assigned_identity" "dumps" {
  name                = "id-houseflow-dumps-writer"
  location            = var.location
  resource_group_name = data.azurerm_resource_group.shared.name
}
