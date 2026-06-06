# ── API (dedicated, isolated POC backend) ─────────────
# Mirrors the preprod API but: (1) no prod-DB clone init container — the POC
# DB is fresh and independent; (2) CORS locked to the Static Web App origin;
# (3) DEMO_MODE=true seeds a demo@demo.com user so the POC is usable out of
# the box. Runs on the shared Container Apps Environment, scales to zero.

resource "azurerm_container_app" "api_poc" {
  name                         = "ca-api-blazor-poc"
  container_app_environment_id = local.main.container_app_environment_id
  resource_group_name          = local.main.resource_group_name
  revision_mode                = "Single"

  identity {
    type         = "UserAssigned"
    identity_ids = [local.main.identity_id]
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
    value = local.pg_connection_poc
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

    # Run EF Core migrations to create the schema on the fresh POC DB.
    init_container {
      name   = "migrate"
      image  = "${local.api_image}:${var.api_image_tag}"
      cpu    = 0.25
      memory = "0.5Gi"
      args   = ["--migrate"]

      env {
        name        = "ConnectionStrings__houseflow"
        secret_name = "db-connection"
      }
      env {
        name  = "AZURE_CLIENT_ID"
        value = local.main.identity_client_id
      }
    }

    container {
      name   = "api"
      image  = "${local.api_image}:${var.api_image_tag}"
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
        value = var.jwt_issuer
      }
      env {
        name  = "Jwt__Audience"
        value = var.jwt_audience
      }
      # Lock CORS to the Static Web App origin (reflect-origin + credentials).
      env {
        name  = "CORS__ORIGINS"
        value = "https://${azurerm_static_web_app.poc.default_host_name}"
      }
      env {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = "Staging"
      }
      # Seed a demo@demo.com user so the POC has a working login.
      env {
        name  = "DEMO_MODE"
        value = "true"
      }
      env {
        name  = "AZURE_CLIENT_ID"
        value = local.main.identity_client_id
      }

      liveness_probe {
        transport = "HTTP"
        path      = "/alive"
        port      = 8080
      }

      readiness_probe {
        transport = "HTTP"
        path      = "/health"
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
