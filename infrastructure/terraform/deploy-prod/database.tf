# ── Database ──────────────────────────────────────────
#
# Seule base créée par ARM : `id-houseflow-prod` est administrateur Entra du
# serveur, les autres bases sont créées en SQL par les jobs dbtools. Le lock
# `CanNotDelete` est posé par le stack `shared`, propriétaire du serveur.

resource "azurerm_postgresql_flexible_server_database" "prod" {
  name      = "${var.project}_prod"
  server_id = data.azurerm_postgresql_flexible_server.shared.id
  charset   = "UTF8"
  collation = "en_US.utf8"
}

locals {
  pg_connection_prod = join(";", [
    "Host=${data.azurerm_postgresql_flexible_server.shared.fqdn}",
    "Port=5432",
    "Database=${azurerm_postgresql_flexible_server_database.prod.name}",
    "Username=${data.azurerm_user_assigned_identity.prod.name}",
    "SSL Mode=Require",
    "Trust Server Certificate=true",
  ])
}
