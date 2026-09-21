# La production : la seule instance permanente.
#
# L'échéance vide est tout ce qui la distingue vraiment. Elle la rend invisible
# au reaper, et c'est d'elle que découlent le verrou du resource group et le
# réplica maintenu — ces deux-là ne se configurent pas, ils se déduisent (voir
# `main.tf`, locals).
#
# Tout le reste est identique à un environnement de PR, et c'est le but : le
# `terraform apply` qui touchera la prod est le même que celui qui a déjà tourné
# dans la PR.

name       = "prod"
expires_at = ""

# Tunnel SSH vers la base, pour le débogage. Une PR n'en a pas : on n'ouvre pas
# de tunnel vers une base qui vit quatre heures.
bastion_enabled = true

demo_mode = false

frontend_host = "www"
api_host      = "api"
