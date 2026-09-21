# ── Réseau de l'environnement ────────────────────────
#
# Le VNet appartient à l'environnement, il n'est plus partagé. C'est ce
# déplacement qui rend un changement de réseau ou de subnet testable : il n'y a
# plus de plan d'adressage commun à préserver, et aucun peering, donc deux
# instances peuvent porter les mêmes plages sans se voir.

resource "azurerm_virtual_network" "env" {
  name                = "vnet-${var.project}-${var.name}"
  location            = azurerm_resource_group.env.location
  resource_group_name = azurerm_resource_group.env.name
  address_space       = [var.vnet_address_space]
  tags                = local.tags
}

# /28 : minimum exigé par Flexible Server.
resource "azurerm_subnet" "db" {
  name                 = "snet-db"
  resource_group_name  = azurerm_resource_group.env.name
  virtual_network_name = azurerm_virtual_network.env.name
  address_prefixes     = [var.db_subnet_prefix]

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
  name                 = "snet-cae"
  resource_group_name  = azurerm_resource_group.env.name
  virtual_network_name = azurerm_virtual_network.env.name
  address_prefixes     = [var.cae_subnet_prefix]

  delegation {
    name = "container-apps"
    service_delegation {
      name    = "Microsoft.App/environments"
      actions = ["Microsoft.Network/virtualNetworks/subnets/join/action"]
    }
  }
}

# Le nom porte celui de l'environnement : une zone privée est unique au sein de
# son resource group, mais deux instances qui partageraient le même nom de zone
# prêteraient à confusion au débogage.
resource "azurerm_private_dns_zone" "postgres" {
  name                = "${var.project}-${var.name}.private.postgres.database.azure.com"
  resource_group_name = azurerm_resource_group.env.name
  tags                = local.tags
}

resource "azurerm_private_dns_zone_virtual_network_link" "postgres" {
  name                  = "vnetlink-${var.project}-${var.name}"
  private_dns_zone_name = azurerm_private_dns_zone.postgres.name
  resource_group_name   = azurerm_resource_group.env.name
  virtual_network_id    = azurerm_virtual_network.env.id
  tags                  = local.tags
}
