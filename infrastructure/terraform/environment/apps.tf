# ── Applications de l'environnement ──────────────────
#
# Chaque environnement porte ses applications : il n'existe plus d'environnement
# qui n'hébergerait que celles des autres. Les hôtes DNS vides désactivent le
# déploiement correspondant, ce qui sert aux essais d'infrastructure pure.

locals {
  api_image      = "ghcr.io/${lower(var.ghcr_username)}/${var.project}-api"
  frontend_fqdn  = var.frontend_host == "" ? null : "${var.frontend_host}.${var.dns_zone}"
  api_fqdn       = var.api_host == "" ? null : "${var.api_host}.${var.dns_zone}"
  api_app_name   = "ca-api-${var.name}"
  deploy_api     = var.api_host != ""
  deploy_web     = var.frontend_host != ""
  jwt_identifier = "HouseFlow-${var.name}"
}

# ── API ──────────────────────────────────────────────

resource "azurerm_container_app" "api" {
  count = local.deploy_api ? 1 : 0

  name                         = local.api_app_name
  container_app_environment_id = azurerm_container_app_environment.env.id
  resource_group_name          = azurerm_resource_group.env.name
  revision_mode                = "Single"
  tags                         = local.tags

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.env.id]
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
    value = local.db_connection_string
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
    min_replicas = local.api_min_replicas
    max_replicas = 1

    init_container {
      name   = "migrate"
      image  = "${local.api_image}:${var.image_tag}"
      cpu    = 0.25
      memory = "0.5Gi"
      args   = ["--migrate"]

      env {
        name        = "ConnectionStrings__houseflow"
        secret_name = "db-connection"
      }
      env {
        name  = "AZURE_CLIENT_ID"
        value = azurerm_user_assigned_identity.env.client_id
      }
    }

    container {
      name   = "api"
      image  = "${local.api_image}:${var.image_tag}"
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
        value = local.jwt_identifier
      }
      env {
        name  = "Jwt__Audience"
        value = local.jwt_identifier
      }
      env {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = local.is_permanent ? "Production" : "Staging"
      }
      env {
        name  = "AZURE_CLIENT_ID"
        value = azurerm_user_assigned_identity.env.client_id
      }
      # Frontend et API sont servis sous la même zone : le cookie de refresh
      # reste sur le SameSite=Lax par défaut.
      env {
        name  = "CORS__ORIGINS"
        value = "https://${local.frontend_fqdn}"
      }
      env {
        name  = "DEMO_MODE"
        value = tostring(var.demo_mode)
      }
      # Hangfire ne tourne qu'en prod (l'unique instance permanente) : ailleurs
      # il ne ferait que polir PostgreSQL en fond pour un job de nettoyage qui
      # n'a rien à nettoyer sur une base éphémère (issue #218).
      env {
        name  = "Hangfire__Enabled"
        value = tostring(local.is_permanent)
      }

      liveness_probe {
        transport = "HTTP"
        path      = "/alive"
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

# ── Frontend ─────────────────────────────────────────
#
# Blazor WebAssembly est entièrement statique : aucun rendu côté serveur, donc
# aucun compute à payer. Une Static Web App en SKU Free coûte 0 $ là où une
# Container App maintenait un réplica en permanence. Le `wwwroot` compilé est
# téléversé par le pipeline avec le jeton de déploiement (`api_key`), et la
# Static Web App émet elle-même son certificat par délégation CNAME — elle ne
# consomme donc pas le wildcard.

resource "azurerm_static_web_app" "frontend" {
  count = local.deploy_web ? 1 : 0

  name                = "swa-${var.name}"
  resource_group_name = azurerm_resource_group.env.name
  location            = azurerm_resource_group.env.location
  sku_tier            = "Free"
  sku_size            = "Free"
  tags                = local.tags
}

# ── Bastion ──────────────────────────────────────────
# Container App scale-to-zero (Alpine + OpenSSH) : ~0 $ à l'arrêt. Tunnel :
#
#   ssh -i <key> -L 5432:<pg_fqdn>:5432 bastion@<bastion_fqdn> -p 2222

resource "azurerm_container_app" "bastion" {
  count = var.bastion_enabled ? 1 : 0

  name                         = "ca-bastion-${var.name}"
  container_app_environment_id = azurerm_container_app_environment.env.id
  resource_group_name          = azurerm_resource_group.env.name
  revision_mode                = "Single"
  tags                         = local.tags

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

      # Le DNS interne des Container Apps ne résout pas les zones privées, et
      # /etc/resolv.conf est monté par kubelet donc non modifiable durablement.
      # Contournement : résoudre le FQDN PostgreSQL via Azure DNS (168.63.129.16)
      # au démarrage et écrire le résultat dans /etc/hosts.
      command = ["/bin/sh", "-c"]
      args    = ["PG_IP=$(nslookup $PG_FQDN 168.63.129.16 2>/dev/null | grep -i 'address' | tail -1 | awk '{print $NF}'); if [ -n \"$PG_IP\" ] && [ \"$PG_IP\" != \"168.63.129.16\" ]; then echo \"$PG_IP $PG_FQDN\" >> /etc/hosts; echo \"Resolved $PG_FQDN -> $PG_IP\"; else echo \"WARNING: Could not resolve $PG_FQDN\"; fi; exec /init"]

      env {
        name  = "PG_FQDN"
        value = azurerm_postgresql_flexible_server.env.fqdn
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
