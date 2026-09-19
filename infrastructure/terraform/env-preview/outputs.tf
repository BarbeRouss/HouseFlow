output "resource_group_name" {
  description = "Resource group de l'environnement"
  value       = module.env.resource_group_name
}

output "vnet_id" {
  description = "ID du VNet de l'environnement"
  value       = module.env.vnet_id
}

output "cae_id" {
  description = "ID du Container Apps Environment"
  value       = module.env.cae_id
}

output "cae_name" {
  description = "Nom du Container Apps Environment"
  value       = module.env.cae_name
}

output "cae_default_domain" {
  description = "Domaine par défaut du Container Apps Environment"
  value       = module.env.cae_default_domain
}

output "cae_custom_domain_verification_id" {
  description = "Valeur des TXT asuid.<hôte>"
  value       = module.env.cae_custom_domain_verification_id
}

output "certificate_id" {
  description = "ID du certificat wildcard sur l'environnement"
  value       = module.env.certificate_id
}

output "identity_id" {
  description = "ID de l'identité managée de l'environnement"
  value       = module.env.identity_id
}

output "identity_client_id" {
  description = "Client ID de l'identité managée"
  value       = module.env.identity_client_id
}

output "identity_name" {
  description = "Nom de l'identité managée"
  value       = module.env.identity_name
}

output "log_analytics_id" {
  description = "ID du workspace Log Analytics"
  value       = module.env.log_analytics_id
}

output "bastion_fqdn" {
  description = "Hôte SSH du bastion (port 2222)"
  value       = module.env.bastion_fqdn
}
