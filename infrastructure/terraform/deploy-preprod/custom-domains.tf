# ── Domaines personnalisés : binding du certificat d'environnement ─────
#
# Le certificat *.houseflow.cloud est porté par le CAE preprod, qui le
# référence directement dans le Key Vault (stack env-preprod). Ici on ne fait
# que lier chaque hostname à ce certificat : aucun certificat géré par hôte,
# aucune attente d'émission.
#
# Prérequis DNS (stack `dns`, appliqué AVANT celui-ci) : pour chaque hôte, un
# CNAME vers le FQDN par défaut de l'app et un TXT asuid.<hôte> portant l'ID de
# vérification du CAE preprod — Azure refuse le hostname sans lui.

resource "azurerm_container_app_custom_domain" "api" {
  name                                     = var.api_domain_preprod
  container_app_id                         = azurerm_container_app.api_preprod.id
  certificate_binding_type                 = "SniEnabled"
  container_app_environment_certificate_id = local.wildcard_certificate_id
}

resource "azurerm_container_app_custom_domain" "frontend" {
  name                                     = var.frontend_domain_preprod
  container_app_id                         = azurerm_container_app.frontend_preprod.id
  certificate_binding_type                 = "SniEnabled"
  container_app_environment_certificate_id = local.wildcard_certificate_id
}
