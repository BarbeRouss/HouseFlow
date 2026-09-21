# ── Identités ────────────────────────────────────────
#
# Deux identités, pour deux besoins qui n'ont pas la même portée.
#
# `id-houseflow-<name>` appartient à l'environnement : elle authentifie l'API
# auprès de son serveur PostgreSQL, et elle est administratrice Entra de ce
# serveur-là. Elle ne demande aucun rôle RBAC Azure — l'habilitation se joue
# côté serveur, dans le resource group de l'environnement.
#
# `id-houseflow-cert` est partagée et permanente : c'est la seule à pouvoir
# lire le secret du certificat wildcard dans le Key Vault. Chaque Container
# Apps Environment l'attache pour la référence Key Vault du certificat.
#
# Cette séparation est ce qui rend un environnement éphémère créable sans
# droit d'attribution de rôle : si l'identité de l'environnement devait lire
# le Key Vault, il faudrait lui poser un `roleAssignments/write` sur une
# ressource du resource group partagé à chaque création — un droit que le rôle
# « HouseFlow Deployer » n'accorde pas, et qu'on ne souhaite pas lui accorder.

resource "azurerm_user_assigned_identity" "env" {
  name                = "id-${var.project}-${var.name}"
  location            = azurerm_resource_group.env.location
  resource_group_name = azurerm_resource_group.env.name
  tags                = local.tags
}

data "azurerm_user_assigned_identity" "certificate" {
  name                = var.certificate_identity_name
  resource_group_name = var.shared_resource_group_name
}
