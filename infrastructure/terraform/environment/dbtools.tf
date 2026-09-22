# ── Données de prod pseudonymisées ───────────────────
#
# L'instance permanente publie chaque nuit un dump pseudonymisé de sa base ; une instance
# jetable le restaure à sa création. Un seul job par instance, et lequel se déduit de la
# permanence, comme le verrou : aucune variable ne peut mettre un `restore` en prod ni un
# `dump` dans une PR.
#
# Le job tourne dans le CAE de l'environnement, seul chemin réseau vers son serveur privé.
# Il porte deux identités : celle de l'environnement pour PostgreSQL (administratrice de
# son serveur), et `id-houseflow-dumps` pour le blob. Cette dernière est lue dans le
# resource group permanent de la souscription, et c'est la souscription qui décide de ses
# droits : écriture côté production, lecture seule côté jetable. Une PR qui détournerait
# ce code ne pourrait donc ni écraser le dump, ni lire une base qui n'est pas la sienne.

locals {
  dbtools_command = local.is_permanent ? "dump" : "restore"
  dbtools_job     = "job-dbtools-${local.dbtools_command}"
  dbtools_image   = "ghcr.io/${lower(var.ghcr_username)}/${var.project}-dbtools:${var.image_tag}"
}

data "azurerm_user_assigned_identity" "dumps" {
  name                = var.dumps_identity_name
  resource_group_name = var.shared_resource_group_name
}

resource "azurerm_container_app_job" "dbtools" {
  name                         = local.dbtools_job
  location                     = azurerm_resource_group.env.location
  resource_group_name          = azurerm_resource_group.env.name
  container_app_environment_id = azurerm_container_app_environment.env.id
  replica_timeout_in_seconds   = 1800
  # Un dump qui échoue sur une donnée non pseudonymisée échouera pareil au second essai,
  # et un restore rejoué est déjà l'affaire du workflow.
  replica_retry_limit = 0
  tags                = local.tags

  identity {
    type = "UserAssigned"
    identity_ids = [
      azurerm_user_assigned_identity.env.id,
      data.azurerm_user_assigned_identity.dumps.id,
    ]
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

  dynamic "schedule_trigger_config" {
    for_each = local.is_permanent ? [1] : []
    content {
      # 02:00 UTC : la prod est au plus calme, et le dump est prêt pour la journée.
      cron_expression          = "0 2 * * *"
      parallelism              = 1
      replica_completion_count = 1
    }
  }

  # Lancé par pr-preview.yml après chaque apply ; le job ne restaure qu'une fois.
  dynamic "manual_trigger_config" {
    for_each = local.is_permanent ? [] : [1]
    content {
      parallelism              = 1
      replica_completion_count = 1
    }
  }

  template {
    container {
      name   = "dbtools"
      image  = local.dbtools_image
      cpu    = 0.5
      memory = "1Gi"
      args   = [local.dbtools_command]

      env {
        name  = "PG_HOST"
        value = azurerm_postgresql_flexible_server.env.fqdn
      }
      env {
        name  = "PG_USER"
        value = azurerm_user_assigned_identity.env.name
      }
      env {
        name  = "PG_DATABASE"
        value = local.database_name
      }
      env {
        name  = "AZURE_CLIENT_ID"
        value = azurerm_user_assigned_identity.env.client_id
      }
      env {
        name  = "DUMPS_CLIENT_ID"
        value = data.azurerm_user_assigned_identity.dumps.client_id
      }
      env {
        name  = "DUMPS_STORAGE_ACCOUNT"
        value = var.dumps_storage_account_name
      }
      env {
        name  = "PRESERVED_EMAILS"
        value = join(",", var.preserved_emails)
      }
    }
  }
}
