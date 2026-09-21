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
