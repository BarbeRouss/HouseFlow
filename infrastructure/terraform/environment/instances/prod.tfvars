# La production : la seule instance permanente.
#
# ttl_hours = 0 la rend invisible au reaper, rg_lock_enabled interdit la
# suppression du resource group et de la base. Tout le reste est identique à
# preprod — c'est le but.

name            = "prod"
ttl_hours       = 0
rg_lock_enabled = true

bastion_enabled = true
deploy_apps     = true
demo_mode       = false

# Un réplica maintenu : pas de démarrage à froid sur la production.
api_min_replicas = 1

frontend_host = "www"
api_host      = "api"
