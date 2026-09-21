# ── Container Apps Environment ───────────────────────

resource "azurerm_log_analytics_workspace" "env" {
  name                = "log-${var.project}-${var.name}"
  location            = azurerm_resource_group.env.location
  resource_group_name = azurerm_resource_group.env.name
  sku                 = "PerGB2018"
  # 30 jours est le plancher du SKU ; sur un environnement jetable les logs
  # disparaissent de toute façon avec le resource group.
  retention_in_days = 30
  tags              = local.tags
}

resource "azurerm_container_app_environment" "env" {
  name                               = "cae-${var.project}-${var.name}"
  location                           = azurerm_resource_group.env.location
  resource_group_name                = azurerm_resource_group.env.name
  log_analytics_workspace_id         = azurerm_log_analytics_workspace.env.id
  infrastructure_subnet_id           = azurerm_subnet.cae.id
  infrastructure_resource_group_name = "ME_cae-${var.project}-${var.name}_${azurerm_resource_group.env.name}_${azurerm_resource_group.env.location}"

  # L'identité partagée du certificat, nécessaire à la référence Key Vault.
  identity {
    type         = "UserAssigned"
    identity_ids = [data.azurerm_user_assigned_identity.certificate.id]
  }

  workload_profile {
    name                  = "Consumption"
    workload_profile_type = "Consumption"
  }

  tags = local.tags

  lifecycle {
    ignore_changes = [infrastructure_resource_group_name]
  }
}

# ── Certificat wildcard par référence Key Vault ──────
#
# azapi et non azurerm : la ressource azurerm_container_app_environment_certificate
# n'accepte qu'un PFX en ligne, pas une référence Key Vault. L'URL du secret est
# volontairement sans version — une nouvelle version émise dans le Key Vault est
# reprise automatiquement, sans redéploiement.
#
# C'est cette référence qui rend le design praticable : Let's Encrypt plafonne
# les certificats identiques à 5 par semaine, donc un environnement éphémère ne
# peut pas émettre le sien. Il emprunte celui du Key Vault, en lecture seule.
#
# L'URI est dérivée du nom plutôt que lue par data source : le Key Vault vit
# dans la souscription de production, et un environnement jetable n'a ainsi
# aucun droit de plan de gestion sur elle. Seule subsiste la lecture du secret,
# au moment de l'exécution, par l'identité du certificat — un droit de plan de
# données sur un secret unique. Dériver plutôt que passer une seconde variable
# supprime la possibilité que le nom et l'URI se contredisent.
resource "azapi_resource" "wildcard_certificate" {
  type      = "Microsoft.App/managedEnvironments/certificates@2025-01-01"
  name      = var.certificate_name
  parent_id = azurerm_container_app_environment.env.id

  body = {
    location = azurerm_resource_group.env.location
    properties = {
      certificateKeyVaultProperties = {
        identity    = data.azurerm_user_assigned_identity.certificate.id
        keyVaultUrl = "https://${var.key_vault_name}.vault.azure.net/secrets/${var.certificate_name}"
      }
    }
  }
}
