# La preprod : l'environnement Azure d'une PR d'infrastructure (#350), dans la souscription jetable.
#
# `name`, `frontend_host`, `api_host` et `expires_at` sont passés en -var par `preprod.yml` : ils
# dépendent du numéro de la PR et de l'heure.
#
# Une PR d'infra reçoit un environnement COMPLET, avec son propre serveur PostgreSQL et son propre
# réseau, créé depuis zéro par le code de la branche : c'est ce qui permet d'éprouver une montée de
# version de PostgreSQL, un changement de SKU ou de sous-réseau AVANT la production. Les ~25 minutes
# de provisionnement se paient à l'ouverture ; les pushes suivants ne redéploient que les images.
#
# Topologie de production : ni `location` ni SKU ne sont redéfinis ici, ce sont les défauts de
# `variables.tf`, ceux de `prod.tfvars`. Décision de Julien (#350) : pas de repli vers une autre
# région ou un autre SKU. Si Azure refuse la capacité (`SkuNotAvailable`, `RegionalAllocationFailed`…),
# le workflow échoue avec un message explicite et la preprod est relancée plus tard — un repli
# éprouverait une topologie qui n'est pas celle de la production.
#
# Il ne reste ici que ce qui diffère de la production.

# Pas de tunnel SSH vers une base qui vit quatre heures et ne contient que des données de démonstration.
bastion_enabled = false

# La base reçoit à sa création le dump pseudonymisé de la nuit (job-dbtools-restore). Le mode démo
# reste utile par-dessus : l'API recrée le compte de démo s'il manque.
demo_mode = true
