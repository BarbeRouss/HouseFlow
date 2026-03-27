# ── Bastion: SSH tunnel for database access ──────────
# Scale-to-zero Container App running Alpine + OpenSSH.
# Use DBeaver SSH tunnel or `ssh -L` to reach PostgreSQL privately.
#
# Usage:
#   ssh -L 5432:psql-houseflow.houseflow.private.postgres.database.azure.com:5432 \
#       bastion@<bastion_fqdn> -p 2222
#   Then connect DBeaver to localhost:5432

resource "azurerm_container_app" "bastion" {
  name                         = "ca-bastion"
  container_app_environment_id = azurerm_container_app_environment.main.id
  resource_group_name          = data.azurerm_resource_group.main.name
  revision_mode                = "Single"

  ingress {
    external_enabled = true
    target_port      = 2222
    transport        = "tcp"
    exposed_port     = 2222

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  secret {
    name  = "ssh-public-key"
    value = var.bastion_ssh_public_key
  }

  template {
    min_replicas = 0
    max_replicas = 1

    container {
      name   = "bastion"
      image  = "lscr.io/linuxserver/openssh-server:version-10.2_p1-r0"
      cpu    = 0.25
      memory = "0.5Gi"

      env {
        name  = "PUID"
        value = "1000"
      }
      env {
        name  = "PGID"
        value = "1000"
      }
      env {
        name  = "TZ"
        value = "Europe/Brussels"
      }
      env {
        name  = "USER_NAME"
        value = "bastion"
      }
      env {
        name        = "PUBLIC_KEY"
        secret_name = "ssh-public-key"
      }
      env {
        name  = "DOCKER_MODS"
        value = "linuxserver/mods:openssh-server-ssh-tunnel"
      }
      env {
        name  = "LISTEN_PORT"
        value = "2222"
      }
    }
  }
}
