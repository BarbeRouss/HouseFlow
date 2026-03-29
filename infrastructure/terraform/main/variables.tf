# ── Core ─────────────────────────────────────────────
variable "subscription_id" {
  description = "Azure subscription ID"
  type        = string
}

variable "location" {
  description = "Azure region"
  type        = string
  default     = "westeurope"
}

variable "project" {
  description = "Project name used as prefix for resources"
  type        = string
  default     = "houseflow"
}

# ── GHCR ─────────────────────────────────────────────
variable "ghcr_username" {
  description = "GitHub username for GHCR authentication"
  type        = string
  default     = "barberouss"
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

# ── Entra ID (Azure AD) ─────────────────────────────
variable "entra_admin_object_id" {
  description = "Object ID of the Azure AD user to set as PostgreSQL Entra admin (for debug access)"
  type        = string
}

variable "entra_admin_name" {
  description = "Display name of the Azure AD user (e.g. your Microsoft account email)"
  type        = string
}

# ── Bastion ──────────────────────────────────────────
variable "bastion_ssh_public_key" {
  description = "SSH public key for bastion access (ssh-rsa ... or ssh-ed25519 ...)"
  type        = string
  sensitive   = true
}
