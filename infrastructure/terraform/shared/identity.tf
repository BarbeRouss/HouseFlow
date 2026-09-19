# ── Identités managées des trois environnements ──────────
# Elles vivent ici pour que ce stack puisse leur poser le RBAC data-plane
# (Key Vault, blob) sans dépendre des stacks `env`, qui se contentent de les
# attacher à leurs Container Apps.

resource "azurerm_user_assigned_identity" "env" {
  for_each = toset(["preprod", "preview", "prod"])

  name                = "id-houseflow-${each.key}"
  location            = var.location
  resource_group_name = data.azurerm_resource_group.shared.name
}
