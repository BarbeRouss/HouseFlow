# ── Réseau partagé : juste ce qu'il faut pour la base ──────────
# Les environnements (preprod, preview, prod) ont leur propre VNet et se
# raccordent ici par peering, créé des deux côtés par leur stack `env`.

resource "azurerm_virtual_network" "shared" {
  name                = "vnet-houseflow-shared"
  location            = var.location
  resource_group_name = data.azurerm_resource_group.shared.name
  address_space       = ["10.0.0.0/24"]
}

# /28 : minimum exigé par Flexible Server.
resource "azurerm_subnet" "db" {
  name                 = "snet-db"
  resource_group_name  = data.azurerm_resource_group.shared.name
  virtual_network_name = azurerm_virtual_network.shared.name
  address_prefixes     = ["10.0.0.0/28"]

  delegation {
    name = "postgresql"
    service_delegation {
      name    = "Microsoft.DBforPostgreSQL/flexibleServers"
      actions = ["Microsoft.Network/virtualNetworks/subnets/join/action"]
    }
  }
}

resource "azurerm_private_dns_zone" "postgres" {
  name                = "houseflow.private.postgres.database.azure.com"
  resource_group_name = data.azurerm_resource_group.shared.name
}

# Les liens vers les VNets d'environnement sont créés par le stack `env`
# correspondant, dans ce même resource group.
resource "azurerm_private_dns_zone_virtual_network_link" "shared" {
  name                  = "vnetlink-shared"
  private_dns_zone_name = azurerm_private_dns_zone.postgres.name
  resource_group_name   = data.azurerm_resource_group.shared.name
  virtual_network_id    = azurerm_virtual_network.shared.id
}
