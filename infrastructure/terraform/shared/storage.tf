# Le storage account porte les states Terraform (conteneurs créés au bootstrap) ;
# on n'y ajoute ici que le conteneur des dumps de base.

data "azurerm_storage_account" "tfstate" {
  name                = "sthouseflowtfstate"
  resource_group_name = data.azurerm_resource_group.shared.name
}

resource "azurerm_storage_container" "db_dumps" {
  name                  = "db-dumps"
  storage_account_id    = data.azurerm_storage_account.tfstate.id
  container_access_type = "private"
}
