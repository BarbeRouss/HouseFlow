variable "subscription_id" {
  description = "Azure subscription ID"
  type        = string
}

variable "dbtools_image_tag" {
  description = "Tag de l'image dbtools"
  type        = string
  default     = "latest"
}

variable "ghcr_pat" {
  description = "PAT GHCR (read:packages) pour tirer l'image dbtools"
  type        = string
  sensitive   = true
  default     = ""
}
