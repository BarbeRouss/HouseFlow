# ── Réseau : un seul VNet pour tout HouseFlow ──────────────────
# Un subnet par Container Apps Environment, plus celui délégué à PostgreSQL.
# Les CAE vivent dans les resource groups d'environnement mais s'attachent à
# ces subnets : leur identité n'a besoin que de `subnets/join/action` ici, au
# lieu des droits de création et de suppression de peerings qu'exigeait un VNet
# par environnement.

resource "azurerm_virtual_network" "main" {
  name                = "vnet-houseflow"
  location            = var.location
  resource_group_name = data.azurerm_resource_group.shared.name
  address_space       = ["10.0.0.0/16"]
}

# /28 : minimum exigé par Flexible Server.
resource "azurerm_subnet" "db" {
  name                 = "snet-db"
  resource_group_name  = data.azurerm_resource_group.shared.name
  virtual_network_name = azurerm_virtual_network.main.name
  address_prefixes     = ["10.0.0.0/28"]

  delegation {
    name = "postgresql"
    service_delegation {
      name    = "Microsoft.DBforPostgreSQL/flexibleServers"
      actions = ["Microsoft.Network/virtualNetworks/subnets/join/action"]
    }
  }
}

# /23 : minimum exigé par un Container Apps Environment en profil Consumption.
resource "azurerm_subnet" "cae" {
  for_each = {
    preprod = "10.0.2.0/23"
    preview = "10.0.4.0/23"
    prod    = "10.0.6.0/23"
  }

  name                 = "snet-cae-${each.key}"
  resource_group_name  = data.azurerm_resource_group.shared.name
  virtual_network_name = azurerm_virtual_network.main.name
  address_prefixes     = [each.value]

  delegation {
    name = "container-apps"
    service_delegation {
      name    = "Microsoft.App/environments"
      actions = ["Microsoft.Network/virtualNetworks/subnets/join/action"]
    }
  }
}

resource "azurerm_private_dns_zone" "postgres" {
  name                = "houseflow.private.postgres.database.azure.com"
  resource_group_name = data.azurerm_resource_group.shared.name
}

# Un seul lien : tous les CAE sont dans ce VNet, donc tous résolvent le FQDN
# privé du serveur.
resource "azurerm_private_dns_zone_virtual_network_link" "main" {
  name                  = "vnetlink-houseflow"
  private_dns_zone_name = azurerm_private_dns_zone.postgres.name
  resource_group_name   = data.azurerm_resource_group.shared.name
  virtual_network_id    = azurerm_virtual_network.main.id
}
