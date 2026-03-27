# ── Virtual Network ──────────────────────────────────

resource "azurerm_virtual_network" "main" {
  name                = "vnet-${var.project}"
  location            = var.location
  resource_group_name = data.azurerm_resource_group.main.name
  address_space       = ["10.0.0.0/16"]
}

# ── Subnet: Container Apps (/23 — required minimum for consumption mode)

resource "azurerm_subnet" "apps" {
  name                 = "snet-apps"
  resource_group_name  = data.azurerm_resource_group.main.name
  virtual_network_name = azurerm_virtual_network.main.name
  address_prefixes     = ["10.0.0.0/23"]

  delegation {
    name = "container-apps"
    service_delegation {
      name    = "Microsoft.App/environments"
      actions = ["Microsoft.Network/virtualNetworks/subnets/join/action"]
    }
  }
}

# ── Subnet: PostgreSQL (/28 — minimum for flexible server)

resource "azurerm_subnet" "db" {
  name                 = "snet-db"
  resource_group_name  = data.azurerm_resource_group.main.name
  virtual_network_name = azurerm_virtual_network.main.name
  address_prefixes     = ["10.0.2.0/28"]

  delegation {
    name = "postgresql"
    service_delegation {
      name    = "Microsoft.DBforPostgreSQL/flexibleServers"
      actions = ["Microsoft.Network/virtualNetworks/subnets/join/action"]
    }
  }
}

# ── Private DNS Zone for PostgreSQL ──────────────────

resource "azurerm_private_dns_zone" "postgres" {
  name                = "${var.project}.private.postgres.database.azure.com"
  resource_group_name = data.azurerm_resource_group.main.name
}

resource "azurerm_private_dns_zone_virtual_network_link" "postgres" {
  name                  = "vnetlink-postgres"
  private_dns_zone_name = azurerm_private_dns_zone.postgres.name
  resource_group_name   = data.azurerm_resource_group.main.name
  virtual_network_id    = azurerm_virtual_network.main.id
}
