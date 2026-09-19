# ── Jobs dbtools ─────────────────────────────────────
# Tout le SQL d'administration passe par ces jobs : le runner GitHub n'a aucun
# chemin réseau vers le serveur. Déclenchement manuel
# (`az containerapp job start`), les variables vides ci-dessous étant surchargées
# au démarrage par `--env-vars`.

locals {
  # Contrat de l'image dbtools : variables surchargées à l'exécution.
  dbtools_runtime_env = {
    roles = {}
    init = {
      PR_NUMBER = ""
      ACTION    = ""
    }
  }
}

resource "azurerm_container_app_job" "dbtools" {
  for_each = toset(var.dbtools_jobs)

  name                         = "job-dbtools-${each.key}"
  location                     = var.location
  resource_group_name          = data.azurerm_resource_group.env.name
  container_app_environment_id = azurerm_container_app_environment.env.id

  replica_timeout_in_seconds = 1800
  replica_retry_limit        = 0

  manual_trigger_config {
    parallelism              = 1
    replica_completion_count = 1
  }

  identity {
    type         = "UserAssigned"
    identity_ids = [data.azurerm_user_assigned_identity.env.id]
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

  template {
    container {
      name    = "dbtools"
      image   = var.dbtools_image
      cpu     = 0.25
      memory  = "0.5Gi"
      command = ["/bin/sh", "-c"]
      args    = ["exec /usr/local/bin/dbtools \"$COMMAND\""]

      env {
        name  = "COMMAND"
        value = each.key
      }
      env {
        name  = "PG_HOST"
        value = data.azurerm_postgresql_flexible_server.main.fqdn
      }
      # L'identité managée est aussi le nom du rôle PostgreSQL.
      env {
        name  = "PG_USER"
        value = data.azurerm_user_assigned_identity.env.name
      }
      env {
        name  = "AZURE_CLIENT_ID"
        value = data.azurerm_user_assigned_identity.env.client_id
      }

      dynamic "env" {
        for_each = local.dbtools_runtime_env[each.key]
        content {
          name  = env.key
          value = env.value
        }
      }
    }
  }
}
