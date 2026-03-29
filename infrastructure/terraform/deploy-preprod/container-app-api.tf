resource "azurerm_container_app" "api_preprod" {
  name                         = "ca-api-preprod"
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
    value = local.main.pg_connection_preprod
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
        set -eo pipefail
        echo "=== Cloning prod DB to preprod ==="

        # Get Entra ID token via managed identity endpoint (auto-injected by Container Apps)
        echo "Acquiring Entra ID token..."
        RESPONSE=$(wget -q -O- --timeout=10 --header="X-IDENTITY-HEADER: $IDENTITY_HEADER" \
          "$IDENTITY_ENDPOINT?resource=https%3A%2F%2Fossrdbms-aad.database.windows.net&api-version=2019-08-01&client_id=$AZURE_CLIENT_ID")
        TOKEN=$(echo "$RESPONSE" | sed 's/.*"access_token":"\([^"]*\)".*/\1/')
        if [ -z "$TOKEN" ] || [ "$TOKEN" = "$RESPONSE" ]; then
          echo "ERROR: Failed to extract access token from identity endpoint"
          exit 1
        fi
        export PGPASSWORD="$TOKEN"
        export PGSSLMODE=require
        echo "Token acquired (${#TOKEN} chars)"

        # Check if prod DB has tables to clone
        PROD_TABLES=$(psql -h "$PG_HOST" -U "$PG_USER" -d "$PROD_DB" -tAc \
          "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public';")
        if [ "$PROD_TABLES" -lt 1 ]; then
          echo "Prod DB has no public tables — skipping clone (migrations will create schema)"
          exit 0
        fi
        echo "Prod DB has $PROD_TABLES public tables"

        # Dump prod and restore to preprod (pipefail ensures pg_dump failures propagate)
        echo "Starting pg_dump | psql..."
        pg_dump -h "$PG_HOST" -U "$PG_USER" -d "$PROD_DB" \
          --clean --if-exists --no-owner --no-acl | \
          psql -h "$PG_HOST" -U "$PG_USER" -d "$PREPROD_DB" -q

        # Verify clone succeeded
        PREPROD_TABLES=$(psql -h "$PG_HOST" -U "$PG_USER" -d "$PREPROD_DB" -tAc \
          "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public';")
        if [ "$PREPROD_TABLES" -lt 1 ]; then
          echo "ERROR: Preprod DB has no public tables after clone — aborting"
          exit 1
        fi
        echo "=== Clone complete ($PREPROD_TABLES public tables) ==="
      EOT
      ]

      env {
        name  = "PG_HOST"
        value = local.main.pg_host
      }
      env {
        name  = "PG_USER"
        value = local.main.identity_name
      }
      env {
        name  = "PROD_DB"
        value = local.main.pg_prod_db_name
      }
      env {
        name  = "PREPROD_DB"
        value = local.main.pg_preprod_db_name
      }
      env {
        name  = "AZURE_CLIENT_ID"
        value = local.main.identity_client_id
      }
    }

    # Step 2: Run EF Core migrations on the cloned data
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
