# ── Managed certificates (free, auto-renewed by Azure) ──

resource "azurerm_container_app_environment_managed_certificate" "api" {
  name                         = "cert-api-prod"
  container_app_environment_id = local.main.container_app_environment_id
  subject_name                 = var.api_domain_prod
  domain_control_validation    = "CNAME"
}

resource "azurerm_container_app_environment_managed_certificate" "frontend" {
  name                         = "cert-frontend-prod"
  container_app_environment_id = local.main.container_app_environment_id
  subject_name                 = var.frontend_domain_prod
  domain_control_validation    = "CNAME"
}

# ── Custom domain bindings ───────────────────────────────

resource "azurerm_container_app_custom_domain" "api" {
  name                                     = trimprefix(var.api_domain_prod, ".")
  container_app_id                         = azurerm_container_app.api_prod.id
  container_app_environment_certificate_id = azurerm_container_app_environment_managed_certificate.api.id
  certificate_binding_type                 = "SniEnabled"
}

resource "azurerm_container_app_custom_domain" "frontend" {
  name                                     = trimprefix(var.frontend_domain_prod, ".")
  container_app_id                         = azurerm_container_app.frontend_prod.id
  container_app_environment_certificate_id = azurerm_container_app_environment_managed_certificate.frontend.id
  certificate_binding_type                 = "SniEnabled"
}
