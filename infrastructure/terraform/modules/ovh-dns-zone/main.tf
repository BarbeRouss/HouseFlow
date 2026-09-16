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
# Ne déclare jamais d'enregistrement avec subdomain = "" (racine de la
# zone) : la redirection apex -> www gérée par OVH hors zone DNS
# (endpoint /domain/zone/{zone}/redirection) doit rester intacte.

resource "ovh_domain_zone_record" "record" {
  for_each = { for r in var.records : "${r.fieldtype}|${r.subdomain}" => r }

  zone      = var.zone_name
  subdomain = each.value.subdomain
  fieldtype = each.value.fieldtype
  ttl       = each.value.ttl
  target    = each.value.target
}
