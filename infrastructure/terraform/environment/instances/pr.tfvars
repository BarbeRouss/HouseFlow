# Les environnements de PR.
#
# `name`, `frontend_host`, `api_host` et `expires_at` sont passés en -var par
# `pr-preview.yml` : ils dépendent du numéro de la PR et de l'heure.
#
# Une PR reçoit un environnement COMPLET, avec son propre serveur PostgreSQL et
# son propre réseau. C'est ce qui permet d'éprouver un changement
# d'infrastructure dans la PR qui l'introduit, au lieu de le découvrir sur la
# prod — et c'est ce qui rend inutile tout environnement de validation séparé.
# Les ~25 minutes de provisionnement se paient à l'ouverture ; les pushes
# suivants ne redéploient que les images.
#
# Il ne reste ici que ce qui ne se déduit pas de l'échéance : le verrou du
# resource group et la charge de l'API en découlent déjà (voir `main.tf`).

# Pas de tunnel SSH vers une base qui vit quatre heures et ne contient que des
# données de démonstration.
bastion_enabled = false

# La base d'une PR reçoit à sa création le dump pseudonymisé de la nuit
# (job-dbtools-restore). Le mode démo reste utile par-dessus : l'API recrée le
# compte de démo s'il manque, et c'est tout ce qu'une PR ouverte avant le premier
# dump aura pour se relire.
demo_mode = true
