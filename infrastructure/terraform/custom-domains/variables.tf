variable "name" {
  description = "Nom de l'instance — prod, ou pr-<n> — dont on lit le state `environment-<name>.tfstate`"
  type        = string
}

variable "tfstate_storage_account_name" {
  description = "Storage account des states de la souscription de l'instance, celui qui porte `environment-<name>.tfstate`"
  type        = string
}
