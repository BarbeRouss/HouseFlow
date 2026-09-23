# ── DNS de l'environnement : calculé ici, posé ailleurs ──────
#
# Les cibles des enregistrements sont des attributs du CAE et de la Static Web
# App de cette racine : c'est donc ici qu'ils se calculent. Mais ils ne s'y
# appliquent plus (#238) :
#
#   environment  → calcule `dns_records`, `api_custom_domain`, `frontend_custom_domain`
#   dns          → écrit les enregistrements dans la zone OVH partagée — seule
#                  racine sous le verrou `ovh-dns-zone`, le temps de quelques
#                  écritures
#   domains      → attend la propagation puis lie les domaines côté Azure, sans
#                  verrou
#
# Quand tout vivait ici, le verrou devait couvrir l'apply entier de cette racine
# (~25 minutes à la création), et les previews en attente se faisaient annuler :
# un groupe de concurrence GitHub ne garde qu'un seul job en attente.
#
# Ne touche jamais l'enregistrement racine ("") : la redirection
# houseflow.cloud -> www.houseflow.cloud est une configuration OVH statique,
# hors Terraform (voir specs/architecture.md).

locals {
  # CNAME de l'API vers le FQDN par défaut de la Container App, dérivable sans
  # attendre l'app, et TXT asuid.<hôte> portant l'ID de vérification du CAE —
  # Azure refuse le hostname personnalisé sans lui.
  api_records = local.deploy_api ? [
    {
      subdomain = var.api_host
      fieldtype = "CNAME"
      target    = "${local.api_app_name}.${azurerm_container_app_environment.env.default_domain}."
      ttl       = local.dns_ttl
    },
    {
      subdomain = "asuid.${var.api_host}"
      fieldtype = "TXT"
      target    = "\"${azurerm_container_app_environment.env.custom_domain_verification_id}\""
      ttl       = local.dns_ttl
    },
  ] : []

  # La Static Web App valide et émet son certificat par délégation CNAME : pas
  # de TXT à poser.
  web_records = local.deploy_web ? [
    {
      subdomain = var.frontend_host
      fieldtype = "CNAME"
      target    = "${azurerm_static_web_app.frontend[0].default_host_name}."
      ttl       = local.dns_ttl
    },
  ] : []

  # TTL court sur un environnement jetable : il se recrée (nouvelle Static Web
  # App, nouveau host par défaut) sans laisser un CNAME périmé en cache.
  dns_ttl = local.is_permanent ? 3600 : 60
}
