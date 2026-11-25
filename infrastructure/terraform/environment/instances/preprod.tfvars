# La preprod (#350) : la version de `main` qui s'apprête à partir en production, déployée par
# `pipeline.yml` (jobs `preprod-*`) dans la souscription Azure jetable, avant l'approbation de la prod.
#
# `name`, `frontend_host`, `api_host`, `expires_at` et `owner_run` sont passés en -var par le workflow :
# l'échéance dépend de l'heure, `owner_run` du run. L'instance a un NOM STABLE (`preprod`), donc jamais
# deux environnements en même temps.
#
# Un environnement COMPLET, avec son propre serveur PostgreSQL et son propre réseau, créé depuis zéro
# par le code de `main` : c'est ce qui permet d'éprouver une montée de version de PostgreSQL, un
# changement de SKU ou de sous-réseau AVANT la production. Les ~25 minutes de provisionnement se
# paient à chaque livraison, en parallèle du plan de production.
#
# Topologie de production : ni `location` ni SKU ne sont redéfinis ici, ce sont les défauts de
# `variables.tf`, ceux de `prod.tfvars`. Décision de Julien (#350) : pas de repli vers une autre
# région ou un autre SKU. Si Azure refuse la capacité (`SkuNotAvailable`, `RegionalAllocationFailed`…),
# le workflow échoue avec un message explicite et la preprod est relancée plus tard (ou sautée avec
# `skip_preprod` sur un déclenchement manuel) — un repli éprouverait une topologie qui n'est pas celle
# de la production.
#
# Il ne reste ici que ce qui diffère de la production.

# Pas de tunnel SSH vers une base qui vit quatre heures et ne contient que des données de démonstration.
bastion_enabled = false

# La base reçoit à sa création le dump pseudonymisé de la nuit (job-dbtools-restore). Le mode démo
# reste utile par-dessus : l'API recrée le compte de démo s'il manque.
demo_mode = true
