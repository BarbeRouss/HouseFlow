# ── DNS OVH de l'environnement ───────────────────────
#
# Chaque environnement pose ses propres enregistrements dans la zone partagée :
# il n'y a plus de stack `dns` centrale qui devait connaître à l'avance tous
# les hôtes durables. La zone reste le seul point de contention entre instances
# — c'est elle que le groupe de concurrence du pipeline sérialise.
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

module "dns" {
  source    = "../modules/ovh-dns-zone"
  zone_name = var.dns_zone
  records   = concat(local.api_records, local.web_records)
}

# Azure valide le TXT asuid.* et le CNAME par résolution DNS publique au moment
# du bind : on laisse la zone OVH se propager avant de tenter.
resource "time_sleep" "dns_propagation" {
  count = local.deploy_api || local.deploy_web ? 1 : 0

  depends_on      = [module.dns]
  create_duration = "60s"
}

resource "azurerm_container_app_custom_domain" "api" {
  count = local.deploy_api ? 1 : 0

  name                                     = local.api_fqdn
  container_app_id                         = azurerm_container_app.api[0].id
  certificate_binding_type                 = "SniEnabled"
  container_app_environment_certificate_id = azapi_resource.wildcard_certificate.id

  depends_on = [time_sleep.dns_propagation]
}

resource "azurerm_static_web_app_custom_domain" "frontend" {
  count = local.deploy_web ? 1 : 0

  static_web_app_id = azurerm_static_web_app.frontend[0].id
  domain_name       = local.frontend_fqdn
  validation_type   = "cname-delegation"

  depends_on = [time_sleep.dns_propagation]
}
