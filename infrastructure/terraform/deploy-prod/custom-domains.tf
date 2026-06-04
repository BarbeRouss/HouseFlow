# ── Custom domains with managed SSL certificates ─────────
#
# Azure Container Apps requires a 3-step process:
#   1. Register hostname on the container app (no cert)
#   2. Create managed certificate (Azure validates CNAME)
#   3. Bind certificate to the hostname (via az CLI)
#
# Prerequisites (DNS records to add BEFORE applying):
#   CNAME  api.houseflow.rouss.be       → <container-app-fqdn>
#   CNAME  houseflow.rouss.be           → <container-app-fqdn>
#   TXT    asuid.api.houseflow.rouss.be → <domain_verification_id>
#   TXT    asuid.houseflow.rouss.be     → <domain_verification_id>
#
# Get the verification ID with:
#   terraform output domain_verification_id

# ── Data source: read environment verification ID ────────

data "azurerm_container_app_environment" "main" {
  name                = "cae-houseflow"
  resource_group_name = local.main.resource_group_name
}

# ── Step 1: Register custom hostnames (no certificate yet) ──

resource "azurerm_container_app_custom_domain" "api" {
  name                     = var.api_domain_prod
  container_app_id         = azurerm_container_app.api_prod.id
  certificate_binding_type = "Disabled"

  lifecycle {
    ignore_changes = [certificate_binding_type, container_app_environment_certificate_id]
  }
}

resource "azurerm_container_app_custom_domain" "frontend" {
  name                     = var.frontend_domain_prod
  container_app_id         = azurerm_container_app.frontend_prod.id
  certificate_binding_type = "Disabled"

  lifecycle {
    ignore_changes = [certificate_binding_type, container_app_environment_certificate_id]
  }
}

# ── Step 2: Create managed certificates (free, auto-renewed) ──

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

  depends_on = [azurerm_container_app_custom_domain.api]
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

  depends_on = [azurerm_container_app_custom_domain.frontend]
}

# ── Step 3: Bind certificates to hostnames (via az CLI) ──
# azapi_update_resource does a PUT that overwrites the entire
# container app config (including secrets), so we use az CLI instead.

resource "terraform_data" "api_cert_binding" {
  triggers_replace = [azapi_resource.cert_api.id]

  provisioner "local-exec" {
    command = <<-EOT
      az containerapp hostname bind \
        --name ca-api-prod \
        --resource-group ${local.main.resource_group_name} \
        --hostname ${var.api_domain_prod} \
        --certificate cert-api-prod \
        --environment cae-houseflow
    EOT
  }
}

resource "terraform_data" "frontend_cert_binding" {
  triggers_replace = [azapi_resource.cert_frontend.id]

  provisioner "local-exec" {
    command = <<-EOT
      az containerapp hostname bind \
        --name ca-frontend-prod \
        --resource-group ${local.main.resource_group_name} \
        --hostname ${var.frontend_domain_prod} \
        --certificate cert-frontend-prod \
        --environment cae-houseflow
    EOT
  }
}
