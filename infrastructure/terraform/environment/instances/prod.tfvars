# La production : la seule instance permanente.
#
# L'échéance vide est tout ce qui la distingue vraiment. Elle la rend invisible
# à tout nettoyage par échéance, et c'est d'elle que découlent le verrou du resource group et le
# réplica maintenu — ces deux-là ne se configurent pas, ils se déduisent (voir
# `main.tf`, locals).
#
# Tout le reste est identique à une instance à échéance (la preprod, #350), et c'est le but :
# le `terraform apply` qui touchera la prod est le même que celui qui aura déjà tourné
# sur la preprod.

name       = "prod"
expires_at = ""

# Tunnel SSH vers la base, pour le débogage. Une instance à échéance n'en a pas :
# on n'ouvre pas de tunnel vers une base qui vit quelques heures.
bastion_enabled = true

demo_mode = false

# Seuls comptes que le dump nocturne pseudonymisé laisse intacts, avec leurs maisons :
# le mainteneur, pour se connecter aux previews avec son compte, et le compte de démo.
preserved_emails = ["julienrousselle@outlook.be", "demo@demo.com"]

frontend_host = "www"
api_host      = "api"
