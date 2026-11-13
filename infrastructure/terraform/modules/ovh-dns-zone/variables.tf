variable "zone_name" {
  description = "Nom de la zone DNS OVH (ex: houseflow.cloud)"
  type        = string
}

variable "records" {
  description = "Enregistrements DNS à créer dans la zone (subdomain = \"\", la racine, seulement en A : l'apex servi par la Container App de l'API, #217)"
  type = list(object({
    subdomain = string
    fieldtype = string
    target    = string
    ttl       = optional(number, 3600)
  }))

  validation {
    condition     = alltrue([for r in var.records : r.subdomain != "" || r.fieldtype == "A"])
    error_message = "Seul un enregistrement A peut viser la racine de la zone (subdomain = \"\") — la redirection web OVH apex vers www est une config statique, hors Terraform, qu'il faut avoir supprimée avant."
  }
}
