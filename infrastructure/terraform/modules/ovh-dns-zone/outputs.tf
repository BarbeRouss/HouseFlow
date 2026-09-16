output "record_ids" {
  description = "IDs des enregistrements DNS créés, indexés par 'fieldtype|subdomain'"
  value       = { for k, r in ovh_domain_zone_record.record : k => r.id }
}
