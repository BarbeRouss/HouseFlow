# ── Core ─────────────────────────────────────────────
variable "subscription_id" {
  description = "Azure subscription ID"
  type        = string
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
variable "api_image_tag" {
  description = "Docker image tag for the API"
  type        = string
  default     = "latest"
}

variable "frontend_image_tag" {
  description = "Docker image tag for the frontend"
  type        = string
  default     = "latest"
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
