# ── Protection de l'instance permanente ──────────────
#
# Absente des environnements jetables, et pas par convention : le verrou se
# déduit de l'absence d'échéance, si bien qu'un environnement éphémère ne peut
# pas en porter. Un lock CanNotDelete sur un resource group promis au reaper
# l'empêcherait de faire son travail — exactement le risque financier que ce
# design écarte.
#
# Le verrou porte sur la base, et PAS sur le resource group. Un lock au niveau
# du groupe couvre tout ce qu'il contient, y compris le subnet délégué : or
# créer un Flexible Server intégré au VNet fait écrire Azure dans ce subnet, et
# l'opération est refusée avec « blocking by customer lock ». Constaté au
# premier déploiement de la production.
#
# Ordonner la création n'y changerait rien : le verrou existerait ensuite, et
# le prochain apply touchant au subnet buterait de la même façon. Une montée de
# version PostgreSQL ou un changement de SKU — ce que cette architecture existe
# précisément pour rendre éprouvable — seraient bloqués par la protection
# elle-même.
#
# Ce qui reste protégé est ce qui compte : les données. Et le resource group ne
# perd pas grand-chose, puisque trois autres barrières le couvrent déjà — il
# vit dans une souscription où aucune identité jetable n'a de rôle, il ne porte
# pas de tag `ttl` donc le reaper ne le voit pas, et `tf-plan-guard.sh` rejette
# tout plan qui détruirait une ressource protégée.

resource "azurerm_management_lock" "database" {
  count = local.db_lock_enabled ? 1 : 0

  name       = "lock-${local.database_name}"
  scope      = azurerm_postgresql_flexible_server_database.env.id
  lock_level = "CanNotDelete"
  notes      = "Base de production — suppression interdite"
}
