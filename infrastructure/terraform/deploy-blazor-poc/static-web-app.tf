# ── Static Web App (Blazor WASM POC) ──────────────────
# Azure Static Web Apps, Free tier: native SPA fallback + static CSP
# (via the app's staticwebapp.config.json) + free managed HTTPS.
# 100% static hosting — no frontend server. Isolated, additive resource.

resource "azurerm_static_web_app" "poc" {
  name                = "swa-${var.project}-blazor-poc"
  resource_group_name = local.main.resource_group_name
  location            = var.swa_location
  sku_tier            = "Free"
  sku_size            = "Free"

  tags = {
    project     = var.project
    environment = "blazor-poc"
    managed_by  = "terraform"
  }
}
