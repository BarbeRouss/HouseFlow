variable "subscription_id" {
  description = "Azure subscription ID"
  type        = string
}

variable "location" {
  description = "Azure region"
  type        = string
  default     = "westeurope"
}

variable "tfstate_storage_account_name" {
  description = "Storage account des states de cette souscription — créé au bootstrap, nom unique au niveau mondial"
  type        = string
}

variable "key_vault_name" {
  description = "Nom du Key Vault portant le certificat wildcard — unique au niveau mondial, et réservé sept jours après une suppression"
  type        = string
  default     = "kv-houseflow"

  validation {
    condition     = can(regex("^[a-zA-Z][a-zA-Z0-9-]{1,22}[a-zA-Z0-9]$", var.key_vault_name))
    error_message = "key_vault_name doit faire 3 à 24 caractères alphanumériques ou tirets, commencer par une lettre et ne pas finir par un tiret."
  }
}
