# ── Connexion PostgreSQL ──────────────────────────────
#
# La base `houseflow_preprod` n'est pas créée ici : le job `dbtools roles`
# (CAE prod, identité `id-houseflow-prod`) la crée en SQL avec
# `id-houseflow-preprod` pour propriétaire. Terraform ne compose donc que la
# chaîne de connexion — auth Entra par identité managée, sans mot de passe :
# l'utilisateur PostgreSQL est le nom de l'identité.

locals {
  pg_database_preprod = "${var.project}_preprod"

  pg_connection_preprod = join(";", [
    "Host=${data.azurerm_postgresql_flexible_server.shared.fqdn}",
    "Port=5432",
    "Database=${local.pg_database_preprod}",
    "Username=${data.azurerm_user_assigned_identity.preprod.name}",
    "SSL Mode=Require",
    "Trust Server Certificate=true",
  ])
}
