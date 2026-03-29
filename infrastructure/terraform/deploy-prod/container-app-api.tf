resource "azurerm_container_app" "api_prod" {
  name                         = "ca-api-prod"
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
    value = local.main.pg_connection_prod
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
    min_replicas = 1
    max_replicas = 1

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
      cpu    = 0.5
      memory = "1Gi"

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
      env {
        name  = "CORS__ORIGINS"
        value = "https://${var.frontend_domain_prod}"
      }
      env {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = "Production"
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

  lifecycle {
    prevent_destroy = true
  }
}
