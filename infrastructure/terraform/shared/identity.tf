# ── L'identité du certificat ─────────────────────────
#
# Seule identité qui subsiste ici. Les identités d'environnement sont parties
# avec le serveur partagé : chacune vit désormais dans le resource group de son
# environnement, où elle n'administre que son propre PostgreSQL.
#
# Celle-ci est partagée et permanente parce qu'elle porte le seul droit qu'un
# environnement ne peut pas se donner lui-même : lire le secret du certificat
# wildcard. Chaque Container Apps Environment l'attache pour sa référence Key
# Vault. Sans elle, créer un environnement éphémère supposerait de lui
# attribuer un rôle sur le Key Vault à chaque création — donc de confier au
# service principal de déploiement le pouvoir de distribuer des rôles.
#
# Une identité, un rôle, attribué une fois. Les environnements n'en héritent
# que l'usage.

resource "azurerm_user_assigned_identity" "certificate" {
  name                = "id-houseflow-cert"
  location            = var.location
  resource_group_name = data.azurerm_resource_group.shared.name
}
