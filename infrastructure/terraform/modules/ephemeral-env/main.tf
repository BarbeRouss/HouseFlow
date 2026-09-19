locals {
  env_name = "pr-${var.pr_number}"

  # Un seul label sous la zone (le wildcard *.houseflow.cloud ne couvre qu'un
  # niveau) : pr-<n> pour le frontend, api-pr-<n> pour l'API.
  frontend_host   = local.env_name
  api_host        = "api-${local.env_name}"
  frontend_domain = "${local.frontend_host}.${var.dns_zone}"
  api_domain      = "${local.api_host}.${var.dns_zone}"
}

# ── API ──────────────────────────────────────────────

resource "azurerm_container_app" "api" {
  name                         = "ca-api-${local.env_name}"
  container_app_environment_id = var.container_app_environment_id
  resource_group_name          = var.resource_group_name
  revision_mode                = "Single"

  identity {
    type         = "UserAssigned"
    identity_ids = [var.identity_id]
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
    value = var.db_connection_string
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

    init_container {
      name   = "migrate"
      image  = "${var.api_image}:${var.image_tag}"
      cpu    = 0.25
      memory = "0.5Gi"
      args   = ["--migrate"]

      env {
        name        = "ConnectionStrings__houseflow"
        secret_name = "db-connection"
      }
      env {
        name  = "AZURE_CLIENT_ID"
        value = var.identity_client_id
      }
    }

    container {
      name   = "api"
      image  = "${var.api_image}:${var.image_tag}"
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
        value = "HouseFlow-PR-${var.pr_number}"
      }
      env {
        name  = "Jwt__Audience"
        value = "HouseFlow-PR-${var.pr_number}"
      }
      env {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = "Staging"
      }
      env {
        name  = "AZURE_CLIENT_ID"
        value = var.identity_client_id
      }
      # Frontend et API sont servis sous houseflow.cloud (même site) : le cookie
      # de refresh reste sur le SameSite=Lax par défaut, comme en prod/preprod.
      env {
        name  = "CORS__ORIGINS"
        value = "https://${local.frontend_domain}"
      }
      env {
        name  = "DEMO_MODE"
        value = "true"
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
# The frontend is a Blazor WebAssembly app — 100% static, no server-side render.
# It is hosted on an Azure Static Web App (Free SKU, $0) instead of a Container
# App: no compute to pay for. The compiled wwwroot is uploaded by the workflow
# with the SWA deployment token (this resource's api_key). The default host name
# is the target of the CNAME pr-<n>.houseflow.cloud below ; the API's CORS
# origin references that custom domain (deterministic — no chicken-and-egg).

resource "azurerm_static_web_app" "frontend" {
  name                = "swa-${local.env_name}"
  resource_group_name = var.resource_group_name
  location            = var.swa_location
  sku_tier            = "Free"
  sku_size            = "Free"
}

# ── DNS + domaines personnalisés ─────────────────────
#
# Même mécanique que prod/preprod (deploy-dns-ovh), mais portée par la preview
# elle-même : CNAME api-pr-<n> vers le FQDN par défaut de l'API (dérivable
# sans attendre l'app), TXT asuid.api-pr-<n> (Azure refuse le hostname sans
# lui), CNAME pr-<n> vers la Static Web App. L'API est liée au certificat
# wildcard de l'environnement ; la Static Web App émet elle-même un
# certificat gratuit pour son domaine (validation par délégation CNAME).

module "dns" {
  source    = "../ovh-dns-zone"
  zone_name = var.dns_zone

  # TTL court : une preview se recrée (nouvelle SWA, nouveau host par défaut)
  # sans laisser un CNAME périmé en cache pendant une heure.
  records = [
    {
      subdomain = local.api_host
      fieldtype = "CNAME"
      target    = "ca-api-${local.env_name}.${var.container_app_environment_domain}."
      ttl       = 60
    },
    {
      subdomain = "asuid.${local.api_host}"
      fieldtype = "TXT"
      target    = "\"${var.custom_domain_verification_id}\""
      ttl       = 60
    },
    {
      subdomain = local.frontend_host
      fieldtype = "CNAME"
      target    = "${azurerm_static_web_app.frontend.default_host_name}."
      ttl       = 60
    },
  ]
}

# Azure valide le TXT asuid.* et le CNAME par résolution DNS publique au
# moment du bind : on laisse la zone OVH se propager avant de tenter.
resource "time_sleep" "dns_propagation" {
  depends_on      = [module.dns]
  create_duration = "60s"
}

resource "azurerm_container_app_custom_domain" "api" {
  name                                     = local.api_domain
  container_app_id                         = azurerm_container_app.api.id
  certificate_binding_type                 = "SniEnabled"
  container_app_environment_certificate_id = var.wildcard_certificate_id

  depends_on = [time_sleep.dns_propagation]
}

resource "azurerm_static_web_app_custom_domain" "frontend" {
  static_web_app_id = azurerm_static_web_app.frontend.id
  domain_name       = local.frontend_domain
  validation_type   = "cname-delegation"

  depends_on = [time_sleep.dns_propagation]
}
