variable "subscription_id" {
  description = "Azure subscription ID"
  type        = string
}

variable "location" {
  description = "Azure region"
  type        = string
  default     = "westeurope"
}

# ── PostgreSQL ───────────────────────────────────────
variable "pg_sku" {
  description = "PostgreSQL Flexible Server SKU"
  type        = string
  default     = "B_Standard_B1ms"
}

variable "pg_storage_mb" {
  description = "PostgreSQL storage in MB"
  type        = number
  default     = 32768 # 32 GB
}

# ── Entra ID ─────────────────────────────────────────
variable "entra_admin_object_id" {
  description = "Object ID de l'utilisateur Entra administrateur PostgreSQL (accès debug via az login + psql)"
  type        = string
}

variable "entra_admin_name" {
  description = "Display name de l'utilisateur Entra (typiquement l'adresse du compte Microsoft)"
  type        = string
}
