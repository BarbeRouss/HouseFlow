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
variable "ghcr_pat" {
  description = "GitHub PAT with read:packages scope for pulling GHCR images"
  type        = string
  sensitive   = true
}

variable "ghcr_username" {
  description = "GitHub username for GHCR authentication"
  type        = string
  default     = "barberouss"
}

# ── Image tags ───────────────────────────────────────
variable "api_image_tag_prod" {
  description = "Docker image tag for the production API"
  type        = string
  default     = "latest"
}

variable "api_image_tag_preprod" {
  description = "Docker image tag for the preprod API"
  type        = string
  default     = "latest"
}

variable "frontend_image_tag_prod" {
  description = "Docker image tag for the production frontend"
  type        = string
  default     = "latest"
}

variable "frontend_image_tag_preprod" {
  description = "Docker image tag for the preprod frontend"
  type        = string
  default     = "latest"
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

# ── Application secrets ──────────────────────────────
variable "jwt_key" {
  description = "JWT signing key (minimum 32 characters)"
  type        = string
  sensitive   = true
}

variable "jwt_issuer" {
  description = "JWT issuer"
  type        = string
  default     = "https://api.houseflow.rouss.be"
}

variable "jwt_audience" {
  description = "JWT audience"
  type        = string
  default     = "https://houseflow.rouss.be"
}

# ── Domains ──────────────────────────────────────────
variable "api_domain_prod" {
  description = "Custom domain for the production API"
  type        = string
  default     = "api.houseflow.rouss.be"
}

variable "frontend_domain_prod" {
  description = "Custom domain for the production frontend"
  type        = string
  default     = "houseflow.rouss.be"
}

# ── Bastion ──────────────────────────────────────────
variable "bastion_ssh_public_key" {
  description = "SSH public key for bastion access (ssh-rsa ... or ssh-ed25519 ...)"
  type        = string
  sensitive   = true
}
