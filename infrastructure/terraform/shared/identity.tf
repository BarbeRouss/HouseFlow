# Seule identité de ce stack, et seule de toute l'infrastructure à porter une
# attribution de rôle. Chaque Container Apps Environment l'attache pour résoudre
# la référence Key Vault de son certificat, au lieu d'utiliser la sienne.
#
# Le raisonnement — pourquoi cette indirection est ce qui rend un environnement
# éphémère créable sans droit d'attribution de rôle — est dans
# `infrastructure/rbac/README.md`, section « L'unique attribution de rôle ».

resource "azurerm_user_assigned_identity" "certificate" {
  name                = "id-houseflow-cert"
  location            = var.location
  resource_group_name = data.azurerm_resource_group.shared.name
}
