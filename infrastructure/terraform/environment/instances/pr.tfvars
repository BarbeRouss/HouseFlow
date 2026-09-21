# Gabarit des environnements de PR.
#
# `name`, `frontend_host` et `api_host` ne sont pas ici : ils dépendent du
# numéro de la PR et sont passés en -var par `pr-preview.yml`
# (`name=pr-123`, `frontend_host=pr-123`, `api_host=api-pr-123`).
# `expires_at` non plus — il glisse à chaque événement de la PR.
#
# Une PR reçoit un environnement COMPLET, avec son propre serveur PostgreSQL et
# son propre réseau. C'est ce qui permet d'éprouver un changement
# d'infrastructure dans la PR qui l'introduit, au lieu de le découvrir sur la
# prod. Les ~25 minutes de provisionnement se paient à l'ouverture ; les pushes
# suivants ne redéploient que les images.

rg_lock_enabled = false

# Pas de bastion : on n'ouvre pas de tunnel SSH vers une base qui vit douze
# heures et ne contient que des données de démonstration.
bastion_enabled = false

# Jeu de données de démonstration : une PR démarre sur une base vide, et une
# preview sans données ne se relit pas. C'est #199 (restauration d'un dump
# pseudonymisé) qui remplacera ce mode par de vraies données anonymisées.
demo_mode = true

# Scale-to-zero : une preview passe l'essentiel de sa vie à ne servir personne.
api_min_replicas = 0
