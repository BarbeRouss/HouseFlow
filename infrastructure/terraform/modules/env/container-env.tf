resource "azurerm_log_analytics_workspace" "env" {
  name                = "log-houseflow-${var.env}"
  location            = var.location
  resource_group_name = data.azurerm_resource_group.env.name
  sku                 = "PerGB2018"
  retention_in_days   = 30
}

resource "azurerm_container_app_environment" "env" {
  name                               = "cae-houseflow-${var.env}"
  location                           = var.location
  resource_group_name                = data.azurerm_resource_group.env.name
  log_analytics_workspace_id         = azurerm_log_analytics_workspace.env.id
  infrastructure_subnet_id           = azurerm_subnet.cae.id
  infrastructure_resource_group_name = "ME_cae-houseflow-${var.env}_${data.azurerm_resource_group.env.name}_${var.location}"

  # Nécessaire à la référence Key Vault du certificat.
  identity {
    type         = "UserAssigned"
    identity_ids = [data.azurerm_user_assigned_identity.env.id]
  }

  workload_profile {
    name                  = "Consumption"
    workload_profile_type = "Consumption"
  }

  lifecycle {
    ignore_changes = [infrastructure_resource_group_name]
  }
}

# ── Certificat wildcard par référence Key Vault ──────────
# azapi et non azurerm : la ressource azurerm_container_app_environment_certificate
# n'accepte qu'un PFX en ligne, pas une référence Key Vault. L'URL du secret est
# volontairement sans version — une nouvelle version émise dans le Key Vault est
# reprise automatiquement, sans redéploiement.
resource "azapi_resource" "wildcard_certificate" {
  type      = "Microsoft.App/managedEnvironments/certificates@2025-01-01"
  name      = var.certificate_name
  parent_id = azurerm_container_app_environment.env.id

  body = {
    location = var.location
    properties = {
      certificateKeyVaultProperties = {
        identity    = data.azurerm_user_assigned_identity.env.id
        keyVaultUrl = "${data.azurerm_key_vault.main.vault_uri}secrets/${var.certificate_name}"
      }
    }
  }
}
