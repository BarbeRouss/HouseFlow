variable "subscription_id" {
  description = "Souscription Azure cible"
  type        = string
}

variable "project" {
  description = "Préfixe de toutes les ressources"
  type        = string
  default     = "houseflow"
}

variable "name" {
  description = "Nom de l'instance — prod, preprod, pr-<n>, ou un nom libre pour un environnement à la demande"
  type        = string

  validation {
    # Ce nom entre dans celui du serveur PostgreSQL (`psql-houseflow-<name>`),
    # qui est un label DNS public et globalement unique : 20 caractères au plus,
    # minuscules, chiffres et tirets, sans tiret en tête ni en queue.
    condition     = can(regex("^[a-z0-9]([a-z0-9-]{0,18}[a-z0-9])?$", var.name))
    error_message = "name doit être en minuscules, 1 à 20 caractères, sans tiret initial ni final."
  }
}

variable "location" {
  description = "Région Azure"
  type        = string
  default     = "westeurope"
}

variable "expires_at" {
  description = "Échéance RFC3339 portée par le tag `ttl`, au-delà de laquelle le reaper détruit l'environnement. Vide = permanent (aucun tag ttl, donc hors de portée du reaper)"
  type        = string
  default     = ""

  validation {
    condition     = var.expires_at == "" || can(formatdate("YYYY-MM-DD", var.expires_at))
    error_message = "expires_at doit être vide ou un timestamp RFC3339 (ex. 2026-09-21T18:00:00Z)."
  }
}

# ── Réseau ───────────────────────────────────────────
# Chaque environnement a son propre VNet et aucun peering : deux instances
# peuvent donc porter le même plan d'adressage sans jamais se gêner. C'est ce
# qui permet de ne pas tenir de registre de plages par environnement.

variable "vnet_address_space" {
  description = "Espace d'adressage du VNet de l'environnement"
  type        = string
  default     = "10.0.0.0/16"
}

variable "db_subnet_prefix" {
  description = "Subnet délégué à PostgreSQL Flexible Server (/28 minimum)"
  type        = string
  default     = "10.0.0.0/28"
}

variable "cae_subnet_prefix" {
  description = "Subnet délégué au Container Apps Environment (/23 minimum en profil Consumption)"
  type        = string
  default     = "10.0.2.0/23"
}

# ── PostgreSQL ───────────────────────────────────────

variable "pg_version" {
  description = "Version majeure de PostgreSQL — c'est précisément ce qu'un environnement jetable permet d'éprouver avant la prod"
  type        = string
  default     = "16"
}

variable "pg_sku" {
  description = "SKU du Flexible Server"
  type        = string
  default     = "B_Standard_B1ms"
}

variable "pg_storage_mb" {
  description = "Stockage du Flexible Server en Mo"
  type        = number
  default     = 32768
}

variable "pg_backup_retention_days" {
  description = "Rétention des sauvegardes"
  type        = number
  default     = 7
}

variable "entra_admin_object_id" {
  description = "Object ID de l'utilisateur administrateur Entra du serveur (accès psql via le bastion)"
  type        = string
}

variable "entra_admin_name" {
  description = "UPN de l'utilisateur administrateur Entra"
  type        = string
}

# ── Options par instance ─────────────────────────────

variable "rg_lock_enabled" {
  description = "Pose un lock CanNotDelete sur le resource group et la base — réservé aux instances permanentes"
  type        = bool
  default     = false
}

variable "bastion_enabled" {
  description = "Déploie le bastion SSH (tunnel vers PostgreSQL)"
  type        = bool
  default     = false
}

variable "bastion_ssh_public_key" {
  description = "Clé publique SSH autorisée sur le bastion"
  type        = string
  sensitive   = true
  default     = ""
}

variable "demo_mode" {
  description = "Active le mode démo de l'API (jeu de données de démonstration)"
  type        = bool
  default     = false
}

variable "api_min_replicas" {
  description = "Réplicas minimum de l'API — 0 pour scale-to-zero sur les environnements jetables"
  type        = number
  default     = 0
}

# ── Applications ─────────────────────────────────────

variable "image_tag" {
  description = "Tag des images applicatives"
  type        = string
  default     = "latest"
}

variable "ghcr_username" {
  description = "Compte GHCR utilisé pour tirer les images"
  type        = string
  default     = "barberouss"
}

variable "ghcr_pat" {
  description = "PAT GHCR (read:packages)"
  type        = string
  sensitive   = true
  default     = ""
}

variable "jwt_key" {
  description = "Clé de signature JWT de l'API"
  type        = string
  sensitive   = true
  default     = ""
}

# ── DNS ──────────────────────────────────────────────

variable "dns_zone" {
  description = "Zone DNS OVH"
  type        = string
  default     = "houseflow.cloud"
}

variable "frontend_host" {
  description = "Label du frontend sous la zone (www pour la prod, preprod pour le miroir). Vide = pas d'enregistrement"
  type        = string
  default     = ""
}

variable "api_host" {
  description = "Label de l'API sous la zone (api, api-preprod). Vide = pas d'enregistrement"
  type        = string
  default     = ""
}

# ── Ressources partagées, lues par nom fixe ──────────

variable "shared_resource_group_name" {
  description = "Resource group permanent de CETTE souscription — porte le storage des states et l'identité du certificat"
  type        = string
  default     = "rg-houseflow-shared"
}

variable "key_vault_uri" {
  description = "URI du Key Vault portant le certificat wildcard, terminée par un slash. Passée en variable et non lue par data source : le coffre vit dans la souscription de production, qu'un environnement jetable ne doit pas pouvoir interroger"
  type        = string

  validation {
    condition     = can(regex("^https://.+/$", var.key_vault_uri))
    error_message = "key_vault_uri doit être une URL https terminée par un slash (ex. https://kv-houseflow.vault.azure.net/)."
  }
}

variable "certificate_name" {
  description = "Nom du certificat wildcard, côté Key Vault comme côté environnement"
  type        = string
  default     = "wildcard-houseflow-cloud"
}

variable "certificate_identity_name" {
  description = "Identité partagée, seule habilitée à lire le secret du certificat dans le Key Vault. Attachée à chaque CAE pour la référence Key Vault, ce qui évite d'avoir à créer un rôle par environnement éphémère"
  type        = string
  default     = "id-houseflow-cert"
}
