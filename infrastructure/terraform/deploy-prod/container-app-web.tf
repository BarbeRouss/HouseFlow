resource "azurerm_container_app" "frontend_prod" {
  name                         = "ca-frontend-prod"
  container_app_environment_id = local.main.container_app_environment_id
  resource_group_name          = local.main.resource_group_name
  revision_mode                = "Single"

  registry {
    server               = "ghcr.io"
    username             = var.ghcr_username
    password_secret_name = "ghcr-pat"
  }

  secret {
    name  = "ghcr-pat"
    value = var.ghcr_pat
  }

  ingress {
    external_enabled = true
    target_port      = 3000
    transport        = "auto"

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  template {
    min_replicas = 1
    max_replicas = 1

    container {
      name   = "frontend"
      image  = "${local.frontend_image}:${var.frontend_image_tag}"
      cpu    = 0.25
      memory = "0.5Gi"

      env {
        name  = "NEXT_PUBLIC_API_URL"
        value = "https://${var.api_domain_prod}"
      }

      liveness_probe {
        transport = "HTTP"
        path      = "/"
        port      = 3000
      }

      startup_probe {
        transport = "HTTP"
        path      = "/"
        port      = 3000
      }
    }
  }

  lifecycle {
    prevent_destroy = true
  }
}
