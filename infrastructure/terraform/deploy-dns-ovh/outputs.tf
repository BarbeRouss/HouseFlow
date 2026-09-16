output "houseflow_cloud_record_ids" {
  description = "IDs des enregistrements DNS créés pour houseflow.cloud"
  value       = module.houseflow_cloud.record_ids
}
