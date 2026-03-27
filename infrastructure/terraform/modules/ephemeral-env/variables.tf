variable "pr_number" {
  description = "Pull request number"
  type        = number
}

variable "resource_group_name" {
  description = "Resource group name"
  type        = string
}

variable "container_app_environment_id" {
  description = "Container Apps Environment ID"
  type        = string
}

variable "api_image" {
  description = "Full API image path (without tag)"
  type        = string
}

variable "frontend_image" {
  description = "Full frontend image path (without tag)"
  type        = string
}

variable "image_tag" {
  description = "Docker image tag for this PR"
  type        = string
}

variable "ghcr_username" {
  description = "GHCR username"
  type        = string
}

variable "ghcr_pat" {
  description = "GHCR PAT for pulling images"
  type        = string
  sensitive   = true
}

variable "db_connection_string" {
  description = "PostgreSQL connection string for the PR database (passwordless)"
  type        = string
  sensitive   = true
}

variable "jwt_key" {
  description = "JWT signing key"
  type        = string
  sensitive   = true
}

variable "identity_id" {
  description = "User-assigned managed identity ID for Entra auth"
  type        = string
}

variable "identity_client_id" {
  description = "Client ID of the managed identity (for AZURE_CLIENT_ID env var)"
  type        = string
}
