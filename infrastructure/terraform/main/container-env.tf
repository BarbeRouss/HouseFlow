resource "azurerm_container_app_environment" "main" {
  name                               = "cae-${var.project}"
  location                           = var.location
  resource_group_name                = data.azurerm_resource_group.main.name
  log_analytics_workspace_id         = azurerm_log_analytics_workspace.main.id
  infrastructure_subnet_id           = azurerm_subnet.apps.id
  infrastructure_resource_group_name = "ME_cae-${var.project}_${data.azurerm_resource_group.main.name}_${var.location}"

  workload_profile {
    name                  = "Consumption"
    workload_profile_type = "Consumption"
  }

  lifecycle {
    ignore_changes = [infrastructure_resource_group_name]
  }
}
