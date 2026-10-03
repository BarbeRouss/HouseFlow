# L'identité de l'environnement n'a aucun rôle RBAC Azure : son habilitation se
# joue côté PostgreSQL, où elle est administratrice Entra de son propre serveur
# (voir `postgresql.tf`). Son nom est aussi celui de son rôle PostgreSQL.

resource "azurerm_user_assigned_identity" "env" {
  name                = "id-${var.project}-${var.name}"
  location            = azurerm_resource_group.env.location
  resource_group_name = azurerm_resource_group.env.name
  tags                = local.tags
}

# Identité partagée, lue dans le resource group permanent de la souscription :
# c'est elle, et non celle ci-dessus, que le CAE attache pour lire le secret du
# certificat wildcard (`infrastructure/rbac/README.md`).
data "azurerm_user_assigned_identity" "certificate" {
  name                = local.certificate_identity_name
  resource_group_name = local.shared_resource_group_name
}
