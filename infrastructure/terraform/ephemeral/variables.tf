# ── Core ─────────────────────────────────────────────
variable "subscription_id" {
  description = "Azure subscription ID"
  type        = string
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

# ── Application secrets ──────────────────────────────
variable "jwt_key" {
  description = "JWT signing key"
  type        = string
  sensitive   = true
}

# ── PR environments ──────────────────────────────────
variable "pr_envs" {
  description = "Map of PR numbers to deploy as ephemeral environments"
  type        = map(object({ image_tag = string }))
  default     = {}
}
