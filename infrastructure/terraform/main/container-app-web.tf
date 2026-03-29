locals {
  frontend_image = "ghcr.io/${local.ghcr_owner}/houseflow-frontend"
}

# ── Production Frontend ──────────────────────────────

resource "azurerm_container_app" "frontend_prod" {
  name                         = "ca-frontend-prod"
  container_app_environment_id = azurerm_container_app_environment.main.id
  resource_group_name          = data.azurerm_resource_group.main.name
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
      image  = "${local.frontend_image}:${var.frontend_image_tag_prod}"
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

# ── Preprod Frontend ─────────────────────────────────

resource "azurerm_container_app" "frontend_preprod" {
  name                         = "ca-frontend-preprod"
  container_app_environment_id = azurerm_container_app_environment.main.id
  resource_group_name          = data.azurerm_resource_group.main.name
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
    min_replicas = 0
    max_replicas = 1

    container {
      name   = "frontend"
      image  = "${local.frontend_image}:${var.frontend_image_tag_preprod}"
      cpu    = 0.25
      memory = "0.5Gi"

      env {
        name  = "NEXT_PUBLIC_API_URL"
        value = "https://${azurerm_container_app.api_preprod.ingress[0].fqdn}"
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
}
