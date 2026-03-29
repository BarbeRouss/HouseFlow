locals {
  ghcr_owner = lower(var.ghcr_username)
  api_image  = "ghcr.io/${local.ghcr_owner}/houseflow-api"
}

# ── Production API ───────────────────────────────────

resource "azurerm_container_app" "api_prod" {
  name                         = "ca-api-prod"
  container_app_environment_id = azurerm_container_app_environment.main.id
  resource_group_name          = data.azurerm_resource_group.main.name
  revision_mode                = "Single"

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.main.id]
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
    value = local.pg_connection_prod
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
      image  = "${local.api_image}:${var.api_image_tag_prod}"
      cpu    = 0.25
      memory = "0.5Gi"
      args   = ["--migrate"]

      env {
        name        = "ConnectionStrings__houseflow"
        secret_name = "db-connection"
      }
      env {
        name  = "AZURE_CLIENT_ID"
        value = azurerm_user_assigned_identity.main.client_id
      }
    }

    container {
      name   = "api"
      image  = "${local.api_image}:${var.api_image_tag_prod}"
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
        value = azurerm_user_assigned_identity.main.client_id
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

# ── Preprod API ──────────────────────────────────────

resource "azurerm_container_app" "api_preprod" {
  name                         = "ca-api-preprod"
  container_app_environment_id = azurerm_container_app_environment.main.id
  resource_group_name          = data.azurerm_resource_group.main.name
  revision_mode                = "Single"

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.main.id]
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
    value = local.pg_connection_preprod
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

    # Step 1: Clone prod DB → preprod DB (validates migrations against real data)
    init_container {
      name   = "clone-prod-db"
      image  = "postgres:16-alpine"
      cpu    = 0.25
      memory = "0.5Gi"

      command = ["/bin/sh", "-c"]
      args = [<<-EOT
        set -e
        echo "=== Cloning prod DB to preprod ==="
        # Get Entra ID token via managed identity endpoint (auto-injected by Container Apps)
        TOKEN=$(wget -q -O- --header="X-IDENTITY-HEADER: $IDENTITY_HEADER" \
          "$IDENTITY_ENDPOINT?resource=https%3A%2F%2Fossrdbms-aad.database.windows.net&api-version=2019-08-01&client_id=$AZURE_CLIENT_ID" \
          | sed 's/.*"access_token":"\([^"]*\)".*/\1/')
        export PGPASSWORD="$TOKEN"
        export PGSSLMODE=require
        echo "Token acquired, starting pg_dump | psql..."
        pg_dump -h "$PG_HOST" -U "$PG_USER" -d "$PROD_DB" \
          --clean --if-exists --no-owner --no-acl 2>/dev/null | \
          psql -h "$PG_HOST" -U "$PG_USER" -d "$PREPROD_DB" -q 2>&1 | tail -5
        echo "=== Clone complete ==="
      EOT
      ]

      env {
        name  = "PG_HOST"
        value = local.pg_host
      }
      env {
        name  = "PG_USER"
        value = azurerm_user_assigned_identity.main.name
      }
      env {
        name  = "PROD_DB"
        value = azurerm_postgresql_flexible_server_database.prod.name
      }
      env {
        name  = "PREPROD_DB"
        value = azurerm_postgresql_flexible_server_database.preprod.name
      }
      env {
        name  = "AZURE_CLIENT_ID"
        value = azurerm_user_assigned_identity.main.client_id
      }
    }

    # Step 2: Run EF Core migrations on the cloned data
    init_container {
      name   = "migrate"
      image  = "${local.api_image}:${var.api_image_tag_preprod}"
      cpu    = 0.25
      memory = "0.5Gi"
      args   = ["--migrate"]

      env {
        name        = "ConnectionStrings__houseflow"
        secret_name = "db-connection"
      }
      env {
        name  = "AZURE_CLIENT_ID"
        value = azurerm_user_assigned_identity.main.client_id
      }
    }

    container {
      name   = "api"
      image  = "${local.api_image}:${var.api_image_tag_preprod}"
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
      env {
        name  = "CORS__ORIGINS"
        value = "*"
      }
      env {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = "Staging"
      }
      env {
        name  = "AZURE_CLIENT_ID"
        value = azurerm_user_assigned_identity.main.client_id
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
