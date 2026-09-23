# Ce que le bootstrap et les workflows doivent connaître de ce stack. Les autres
# valeurs se déduisent d'un nom ou sont des constantes : les exposer en sortie
# n'aurait fait qu'ajouter un endroit de plus où elles peuvent diverger.

output "key_vault_name" {
  description = "À reporter dans la variable de dépôt KEY_VAULT_NAME"
  value       = azurerm_key_vault.main.name
}

output "certificate_identity_name" {
  description = "Identité que chaque CAE attache pour lire le secret du certificat — son homologue doit exister dans la souscription des environnements jetables"
  value       = azurerm_user_assigned_identity.certificate.name
}

output "db_dumps_container_name" {
  value = azurerm_storage_container.db_dumps.name
}

output "dumps_identity_name" {
  description = "Identité que le job de dump de la prod attache pour publier dans db-dumps — son homologue en lecture seule, id-houseflow-dumps-reader, doit exister dans la souscription des environnements jetables"
  value       = azurerm_user_assigned_identity.dumps.name
}
