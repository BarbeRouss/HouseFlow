# Preprod : exceptionnelle, à la demande, au plus près de la production.
#
# Elle n'est pas une étape du pipeline et n'est créée par aucun push. On la
# lance à la main (workflow `environment.yml`) quand on veut éprouver un
# changement d'infrastructure sur un environnement qui ressemble à la prod
# jusque dans ses réglages de charge — ce qu'un environnement de PR, réglé pour
# être bon marché, ne fait pas.
#
# Le chemin normal pour valider un changement d'infrastructure reste d'ouvrir
# la PR : elle crée déjà un environnement complet. Preprod sert à ce qui n'est
# pas encore un changement de code (essayer une version majeure de PostgreSQL
# avant d'écrire la ligne) ou à ce qui doit vivre plus longtemps qu'une PR.
#
# `expires_at` n'est pas ici : le workflow le calcule à chaque apply. Le laisser
# vide signifierait « permanent », exactement le contraire de ce qu'on veut, et
# le figer à une date le rendrait périmé au deuxième apply.

name = "preprod"

# Pas de lock : preprod doit pouvoir être détruite, par le workflow comme par
# le reaper. C'est la seule différence de fond avec la prod.
rg_lock_enabled = false

bastion_enabled = true
demo_mode       = false

# Comme la prod, et non scale-to-zero : un réglage de charge qui diffère
# fausserait précisément ce qu'on vient mesurer ici.
api_min_replicas = 1

frontend_host = "preprod"
api_host      = "api-preprod"
