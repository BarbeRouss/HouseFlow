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

# ── PR environment ───────────────────────────────────
variable "pr_number" {
  description = "Pull request number for this ephemeral environment"
  type        = number
}

variable "image_tag" {
  description = "Docker image tag for this PR (e.g. pr-42)"
  type        = string
}
