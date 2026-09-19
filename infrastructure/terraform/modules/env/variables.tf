variable "env" {
  description = "Nom de l'environnement (preprod, preview, prod) — suffixe de toutes les ressources"
  type        = string

  validation {
    condition     = contains(["preprod", "preview", "prod"], var.env)
    error_message = "env doit valoir preprod, preview ou prod."
  }
}

variable "resource_group_name" {
  description = "Resource group de l'environnement (créé au bootstrap)"
  type        = string
}

variable "location" {
  description = "Azure region"
  type        = string
  default     = "westeurope"
}

# ── Réseau ───────────────────────────────────────────
variable "address_space" {
  description = "Espace d'adressage du VNet de l'environnement (ex. 10.1.0.0/16)"
  type        = string
}

variable "cae_subnet_prefix" {
  description = "Préfixe du subnet Container Apps — /23 minimum en profil Consumption"
  type        = string
}

# ── Options par environnement ────────────────────────
variable "bastion_enabled" {
  description = "Déploie le bastion SSH (tunnel vers PostgreSQL)"
  type        = bool
  default     = false
}

variable "bastion_ssh_public_key" {
  description = "Clé publique SSH autorisée sur le bastion — peut rester vide si bastion_enabled = false"
  type        = string
  sensitive   = true
  default     = ""
}

variable "rg_lock_enabled" {
  description = "Pose un lock CanNotDelete sur le resource group"
  type        = bool
  default     = false
}

# ── Jobs dbtools ─────────────────────────────────────
variable "dbtools_jobs" {
  description = "Sous-commandes dbtools à déployer en Container App Job manuel (roles, init)"
  type        = list(string)
  default     = []

  validation {
    condition     = length(setsubtract(toset(var.dbtools_jobs), toset(["roles", "init"]))) == 0
    error_message = "dbtools_jobs n'accepte que roles et init."
  }
}

variable "dbtools_image" {
  description = "Image dbtools complète, tag inclus (ghcr.io/barberouss/houseflow-dbtools:<tag>)"
  type        = string
  default     = ""
}

variable "ghcr_username" {
  description = "Compte GHCR utilisé pour tirer l'image dbtools"
  type        = string
  default     = "barberouss"
}

variable "ghcr_pat" {
  description = "PAT GHCR (read:packages) — requis seulement si dbtools_jobs n'est pas vide"
  type        = string
  sensitive   = true
  default     = ""
}

# ── Ressources partagées, lues par data source sur nom fixe ──
variable "shared_resource_group_name" {
  description = "Resource group partagé"
  type        = string
  default     = "rg-houseflow-shared"
}

variable "shared_vnet_name" {
  description = "VNet partagé, cible du peering"
  type        = string
  default     = "vnet-houseflow-shared"
}

variable "private_dns_zone_name" {
  description = "Zone DNS privée du serveur PostgreSQL"
  type        = string
  default     = "houseflow.private.postgres.database.azure.com"
}

variable "key_vault_name" {
  description = "Key Vault portant le certificat wildcard"
  type        = string
  default     = "kv-houseflow"
}

variable "certificate_name" {
  description = "Nom du certificat wildcard, côté Key Vault comme côté environnement"
  type        = string
  default     = "wildcard-houseflow-cloud"
}

variable "pg_server_name" {
  description = "Serveur PostgreSQL partagé"
  type        = string
  default     = "psql-houseflow"
}
