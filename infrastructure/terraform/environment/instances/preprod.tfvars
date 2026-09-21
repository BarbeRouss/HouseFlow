# Le miroir de la production : même racine, même apply, autre resource group.
#
# Sa raison d'être est de valider l'infrastructure de bout en bout avant la
# prod — y compris sa création depuis zéro, ce qu'un environnement permanent
# finirait par ne plus prouver en dérivant d'apply correctif en apply correctif.
# D'où le TTL : preprod est recréée pour chaque validation, puis détruite.

name = "preprod"

# `expires_at` n'est pas ici : le pipeline le calcule à chaque apply. Le laisser
# vide signifierait « permanent », exactement le contraire de ce qu'on veut, et
# le figer à une date le rendrait périmé au deuxième apply. Le pipeline détruit
# preprod dès la validation passée ; le tag n'est que le filet du reaper si la
# destruction n'a pas lieu (run annulé, job écroulé).
rg_lock_enabled = false

bastion_enabled = true
deploy_apps     = true
demo_mode       = false

# Scale-to-zero : preprod ne sert personne entre deux validations.
api_min_replicas = 0

frontend_host = "preprod"
api_host      = "api-preprod"
