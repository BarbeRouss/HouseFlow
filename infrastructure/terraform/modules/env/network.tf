resource "azurerm_virtual_network" "env" {
  name                = "vnet-houseflow-${var.env}"
  location            = var.location
  resource_group_name = data.azurerm_resource_group.env.name
  address_space       = [var.address_space]
}

# /23 : minimum exigé par un Container Apps Environment en profil Consumption.
resource "azurerm_subnet" "cae" {
  name                 = "snet-cae"
  resource_group_name  = data.azurerm_resource_group.env.name
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

# ── Peering vers le VNet partagé ─────────────────────
# Les deux sens sont créés ici : le rôle "HouseFlow Shared Tenant" autorise
# l'environnement à écrire le peering retour dans rg-houseflow-shared.

resource "azurerm_virtual_network_peering" "env_to_shared" {
  name                         = "peer-${var.env}-to-shared"
  resource_group_name          = data.azurerm_resource_group.env.name
  virtual_network_name         = azurerm_virtual_network.env.name
  remote_virtual_network_id    = data.azurerm_virtual_network.shared.id
  allow_virtual_network_access = true
  allow_forwarded_traffic      = false
  allow_gateway_transit        = false
  use_remote_gateways          = false
}

resource "azurerm_virtual_network_peering" "shared_to_env" {
  name                         = "peer-shared-to-${var.env}"
  resource_group_name          = var.shared_resource_group_name
  virtual_network_name         = data.azurerm_virtual_network.shared.name
  remote_virtual_network_id    = azurerm_virtual_network.env.id
  allow_virtual_network_access = true
  allow_forwarded_traffic      = false
  allow_gateway_transit        = false
  use_remote_gateways          = false
}

# Sans ce lien, le FQDN privé du serveur PostgreSQL ne résout pas depuis le CAE.
resource "azurerm_private_dns_zone_virtual_network_link" "env" {
  name                  = "vnetlink-${var.env}"
  private_dns_zone_name = data.azurerm_private_dns_zone.postgres.name
  resource_group_name   = var.shared_resource_group_name
  virtual_network_id    = azurerm_virtual_network.env.id
  registration_enabled  = false
}
