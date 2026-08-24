locals {
  env_name = "pr-${var.pr_number}"
}

# ── API ──────────────────────────────────────────────

resource "azurerm_container_app" "api" {
  name                         = "ca-api-${local.env_name}"
  container_app_environment_id = var.container_app_environment_id
  resource_group_name          = var.resource_group_name
  revision_mode                = "Single"

  identity {
    type         = "UserAssigned"
    identity_ids = [var.identity_id]
  }

  registry {
    server               = "ghcr.io"
    username             = var.ghcr_username
    password_secret_name = "ghcr-pat"
  }

  secret {
    name  = "ghcr-pat"
    value = var.ghcr_pat
  }

  secret {
    name  = "db-connection"
    value = var.db_connection_string
  }

  secret {
    name  = "jwt-key"
    value = var.jwt_key
  }

  ingress {
    external_enabled = true
    target_port      = 8080
    transport        = "auto"

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  template {
    min_replicas = 0
    max_replicas = 1

    init_container {
      name   = "migrate"
      image  = "${var.api_image}:${var.image_tag}"
      cpu    = 0.25
      memory = "0.5Gi"
      args   = ["--migrate"]

      env {
        name        = "ConnectionStrings__houseflow"
        secret_name = "db-connection"
      }
      env {
        name  = "AZURE_CLIENT_ID"
        value = var.identity_client_id
      }
    }

    container {
      name   = "api"
      image  = "${var.api_image}:${var.image_tag}"
      cpu    = 0.25
      memory = "0.5Gi"

      env {
        name        = "ConnectionStrings__houseflow"
        secret_name = "db-connection"
      }
      env {
        name        = "JWT__KEY"
        secret_name = "jwt-key"
      }
      env {
        name  = "Jwt__Issuer"
        value = "HouseFlow-PR-${var.pr_number}"
      }
      env {
        name  = "Jwt__Audience"
        value = "HouseFlow-PR-${var.pr_number}"
      }
      env {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = "Staging"
      }
      env {
        name  = "AZURE_CLIENT_ID"
        value = var.identity_client_id
      }
      env {
        name  = "CORS__ORIGINS"
        value = "https://${azurerm_static_web_app.frontend.default_host_name}"
      }
      env {
        name  = "DEMO_MODE"
        value = "true"
      }

      liveness_probe {
        transport = "HTTP"
        path      = "/alive"
        port      = 8080
      }

      startup_probe {
        transport = "HTTP"
        path      = "/alive"
        port      = 8080
      }
    }
  }
}

# ── Frontend ─────────────────────────────────────────
# The frontend is a Blazor WebAssembly app — 100% static, no server-side render.
# It is hosted on an Azure Static Web App (Free SKU, $0) instead of a Container
# App: no compute to pay for. The compiled wwwroot is uploaded by the workflow
# with the SWA deployment token (this resource's api_key). The default host name
# is known at apply time, so the API's CORS origin above can reference it
# directly (deterministic — no chicken-and-egg).

resource "azurerm_static_web_app" "frontend" {
  name                = "swa-${local.env_name}"
  resource_group_name = var.resource_group_name
  location            = var.swa_location
  sku_tier            = "Free"
  sku_size            = "Free"
}
