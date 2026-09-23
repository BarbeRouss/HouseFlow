# Sept sorties sont lues par les workflows, les trois autres servent au
# débogage à la main. Les sorties qui n'avaient ni l'un ni l'autre usage ont été
# retirées : une sortie que personne ne lit ne documente rien, elle se périme.

# ── Lues par les workflows ───────────────────────────

output "api_url" {
  value = local.deploy_api ? "https://${local.api_fqdn}" : null
}

output "demo_mode" {
  description = "Repris tel quel dans la configuration d'exécution du frontend — que les workflows le lisent ici plutôt que de recopier `true` ou `false` garde les tfvars seule source de vérité"
  value       = tostring(var.demo_mode)
}

output "frontend_url" {
  value = local.deploy_web ? "https://${local.frontend_fqdn}" : null
}

output "dbtools_job_name" {
  description = "job-dbtools-dump sur l'instance permanente, job-dbtools-restore sur une instance jetable"
  value       = azurerm_container_app_job.dbtools.name
}

output "log_analytics_workspace_id" {
  description = "Customer id du workspace Log Analytics du CAE — les workflows y relisent les logs des jobs dbtools"
  value       = azurerm_log_analytics_workspace.env.workspace_id
}

output "api_app_name" {
  description = "Container App de l'API — redémarrée après une restauration pour rejouer les migrations de la branche"
  value       = local.deploy_api ? azurerm_container_app.api[0].name : null
}

output "static_web_app_api_key" {
  description = "Jeton de déploiement de la Static Web App — les workflows téléversent le wwwroot compilé avec"
  value       = local.deploy_web ? azurerm_static_web_app.frontend[0].api_key : null
  sensitive   = true
}

# ── Pour inspecter un environnement à la main ────────

output "resource_group_name" {
  description = "Cible des commandes az, et du reaper"
  value       = azurerm_resource_group.env.name
}

output "postgresql_fqdn" {
  description = "Hôte du tunnel SSH : ssh -L 5432:<ce fqdn>:5432 bastion@<fqdn du bastion> -p 2222"
  value       = azurerm_postgresql_flexible_server.env.fqdn
}

output "identity_name" {
  description = "Identité de l'environnement, qui est aussi le nom de son rôle PostgreSQL — donc l'utilisateur à passer à psql"
  value       = azurerm_user_assigned_identity.env.name
}
