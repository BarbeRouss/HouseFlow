# ── Bastion: SSH tunnel for database access ──────────
# Scale-to-zero Container App running Alpine + OpenSSH.
# Use DBeaver SSH tunnel or `ssh -L` to reach PostgreSQL privately.
#
# Usage:
#   ssh -i <key> -L 5432:psql-houseflow.houseflow.private.postgres.database.azure.com:5432 \
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

      # Container Apps internal DNS can't resolve private DNS zones, and
      # /etc/resolv.conf is kubelet-mounted so can't be overwritten persistently.
      # Workaround: resolve the PostgreSQL FQDN via Azure DNS (168.63.129.16)
      # using busybox nslookup at startup and write the result to /etc/hosts.
      command = ["/bin/sh", "-c"]
      args    = ["PG_IP=$(nslookup $PG_FQDN 168.63.129.16 2>/dev/null | grep -i 'address' | tail -1 | awk '{print $NF}'); if [ -n \"$PG_IP\" ] && [ \"$PG_IP\" != \"168.63.129.16\" ]; then echo \"$PG_IP $PG_FQDN\" >> /etc/hosts; echo \"Resolved $PG_FQDN -> $PG_IP\"; else echo \"WARNING: Could not resolve $PG_FQDN\"; fi; exec /init"]

      env {
        name  = "PG_FQDN"
        value = azurerm_postgresql_flexible_server.main.fqdn
      }
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
