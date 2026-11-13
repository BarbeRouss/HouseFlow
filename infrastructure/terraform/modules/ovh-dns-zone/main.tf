terraform {
  required_providers {
    ovh = {
      source  = "ovh/ovh"
      version = "~> 2.0"
    }
  }
}

# ── DNS records ───────────────────────────────────────
#
# Le seul enregistrement racine (subdomain = "") admis est un A (#217, apex servi par la
# Container App de l'API). La redirection apex -> www gérée par OVH hors zone DNS (endpoint
# /domain/zone/{zone}/redirection) pose elle-même un A à la racine : elle doit être supprimée
# à la main avant de poser celui-ci, et le module ne la touche jamais.

resource "ovh_domain_zone_record" "record" {
  for_each = { for r in var.records : "${r.fieldtype}|${r.subdomain}" => r }

  zone      = var.zone_name
  subdomain = each.value.subdomain
  fieldtype = each.value.fieldtype
  ttl       = each.value.ttl
  target    = each.value.target
}
