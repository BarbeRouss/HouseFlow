# ── Domaines personnalisés : binding du certificat wildcard ─────
#
# Le certificat *.houseflow.cloud est émis et mis à disposition sur
# l'environnement par .github/workflows/certificate.yml (Key Vault en copie
# durable). Ici on ne fait que lier chaque hostname à ce certificat : plus de
# certificat géré par hôte, plus d'attente d'émission, plus d'étape `az` en
# local-exec.
#
# Prérequis DNS (deploy-dns-ovh, appliqué AVANT ce stack) : pour chaque hôte,
# un CNAME vers le FQDN par défaut de l'app et un TXT asuid.<hôte> portant l'ID
# de vérification de l'environnement — Azure refuse le hostname sans lui.

resource "azurerm_container_app_custom_domain" "api" {
  name                                     = var.api_domain_preprod
  container_app_id                         = azurerm_container_app.api_preprod.id
  certificate_binding_type                 = "SniEnabled"
  container_app_environment_certificate_id = local.main.wildcard_certificate_id
}

resource "azurerm_container_app_custom_domain" "frontend" {
  name                                     = var.frontend_domain_preprod
  container_app_id                         = azurerm_container_app.frontend_preprod.id
  certificate_binding_type                 = "SniEnabled"
  container_app_environment_certificate_id = local.main.wildcard_certificate_id
}
