data "azurerm_resource_group" "main" {
  name = "rg-${var.project}"
}

## Management lock is managed via Azure CLI in the deploy workflow
## (not Terraform) because CanNotDelete blocks Terraform's own resource
## deletions (e.g. PR preview cleanup). The workflow removes the lock
## before apply and recreates it after.
