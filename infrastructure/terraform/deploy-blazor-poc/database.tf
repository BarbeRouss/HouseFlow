# ── Database ──────────────────────────────────────────
# Dedicated, isolated POC database on the shared PostgreSQL Flexible Server.
# Fresh schema created by EF migrations; seeded with a demo user (DEMO_MODE).
# No prod clone — fully independent of prod/preprod data.
# No management lock — the POC stack must remain destroyable (terraform destroy).

resource "azurerm_postgresql_flexible_server_database" "poc" {
  name      = "${var.project}_blazor_poc"
  server_id = local.main.pg_server_id
  charset   = "UTF8"
  collation = "en_US.utf8"
}

locals {
  pg_connection_poc = join(";", [
    "Host=${local.main.pg_host}",
    "Port=5432",
    "Database=${azurerm_postgresql_flexible_server_database.poc.name}",
    "Username=${local.main.identity_name}",
    "SSL Mode=Require",
    "Trust Server Certificate=true",
  ])
}
