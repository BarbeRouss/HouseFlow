variable "subscription_id" {
  description = "Azure subscription ID"
  type        = string
}

variable "project" {
  description = "Project name used as prefix for resources"
  type        = string
  default     = "houseflow"
}

variable "dns_zone" {
  description = "Zone DNS OVH portant les hôtes durables"
  type        = string
  default     = "houseflow.cloud"
}
