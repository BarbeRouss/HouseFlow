variable "zone_name" {
  description = "Nom de la zone DNS OVH (ex: houseflow.cloud)"
  type        = string
}

variable "records" {
  description = "Enregistrements DNS à créer dans la zone (jamais subdomain = \"\", réservé à la redirection apex OVH statique)"
  type = list(object({
    subdomain = string
    fieldtype = string
    target    = string
    ttl       = optional(number, 3600)
  }))

  validation {
    condition     = alltrue([for r in var.records : r.subdomain != ""])
    error_message = "Ce module ne doit jamais gérer l'enregistrement racine (subdomain = \"\") — la redirection apex vers www est une config OVH statique, hors Terraform."
  }
}
