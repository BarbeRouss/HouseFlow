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

# ── Data source: read environment verification ID ────────
# Exposé en output (domain_verification_id) pour diagnostic.

data "azurerm_container_app_environment" "main" {
  name                = "cae-houseflow"
  resource_group_name = local.main.resource_group_name
}

resource "azurerm_container_app_custom_domain" "api" {
  name                                     = var.api_domain_prod
  container_app_id                         = azurerm_container_app.api_prod.id
  certificate_binding_type                 = "SniEnabled"
  container_app_environment_certificate_id = local.main.wildcard_certificate_id
}

resource "azurerm_container_app_custom_domain" "frontend" {
  name                                     = var.frontend_domain_prod
  container_app_id                         = azurerm_container_app.frontend_prod.id
  certificate_binding_type                 = "SniEnabled"
  container_app_environment_certificate_id = local.main.wildcard_certificate_id
}

# ── Anciens certificats gérés par hôte ───────────────────────────
# Conservés le temps que les hostnames ci-dessus soient rebindés sur le
# wildcard : Azure refuse de supprimer un certificat encore lié. `ignore_changes`
# évite qu'un changement de nom d'hôte (bascule rouss.be → houseflow.cloud) ne
# force leur remplacement. À supprimer (avec le provider azapi) une fois la
# bascule appliquée.

resource "azapi_resource" "cert_api" {
  type      = "Microsoft.App/managedEnvironments/managedCertificates@2024-03-01"
  name      = "cert-api-prod"
  parent_id = local.main.container_app_environment_id
  location  = "westeurope"

  body = {
    properties = {
      subjectName             = var.api_domain_prod
      domainControlValidation = "CNAME"
    }
  }

  lifecycle {
    ignore_changes = [body]
  }
}

resource "azapi_resource" "cert_frontend" {
  type      = "Microsoft.App/managedEnvironments/managedCertificates@2024-03-01"
  name      = "cert-frontend-prod"
  parent_id = local.main.container_app_environment_id
  location  = "westeurope"

  body = {
    properties = {
      subjectName             = var.frontend_domain_prod
      domainControlValidation = "CNAME"
    }
  }

  lifecycle {
    ignore_changes = [body]
  }
}
