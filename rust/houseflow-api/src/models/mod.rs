//! Structures de lignes : une par table de la base (noms PascalCase entre guillemets,
//! identiques au schéma EF).
//!
//! Convention unique dans tout le crate : les champs sont en `snake_case` et
//! `#[sqlx(rename_all = "PascalCase")]` fait la correspondance avec les colonnes.
//! Les colonnes `varchar` portant une énumération passent par
//! `#[sqlx(try_from = "String")]`, les `integer` par `#[sqlx(try_from = "i32")]`.

pub mod enums;

use chrono::{DateTime, Utc};
use rust_decimal::Decimal;
use sqlx::FromRow;
use uuid::Uuid;

pub use enums::{ApiKeyScope, HouseRole, InvitationStatus, ParseEnumError, Periodicity};

/// Table `"Users"`.
#[derive(Debug, Clone, FromRow)]
#[sqlx(rename_all = "PascalCase")]
pub struct User {
    pub id: Uuid,
    pub email: String,
    pub password_hash: String,
    pub created_at: DateTime<Utc>,
    pub updated_at: Option<DateTime<Utc>>,
    pub first_name: String,
    pub last_name: String,
    pub language: String,
    pub theme: String,
    pub is_admin: bool,
}

/// Table `"Houses"`.
#[derive(Debug, Clone, FromRow)]
#[sqlx(rename_all = "PascalCase")]
pub struct House {
    pub id: Uuid,
    pub name: String,
    pub address: Option<String>,
    pub zip_code: Option<String>,
    pub city: Option<String>,
    pub country: Option<String>,
    pub created_at: DateTime<Utc>,
    pub updated_at: Option<DateTime<Utc>>,
    pub user_id: Uuid,
}

/// Table `"Devices"`.
#[derive(Debug, Clone, FromRow)]
#[sqlx(rename_all = "PascalCase")]
pub struct Device {
    pub id: Uuid,
    pub name: String,
    #[sqlx(rename = "Type")]
    pub device_type: String,
    pub install_date: Option<DateTime<Utc>>,
    pub created_at: DateTime<Utc>,
    pub updated_at: Option<DateTime<Utc>>,
    pub house_id: Uuid,
    pub brand: Option<String>,
    pub model: Option<String>,
}

/// Table `"MaintenanceTypes"`.
#[derive(Debug, Clone, FromRow)]
#[sqlx(rename_all = "PascalCase")]
pub struct MaintenanceType {
    pub id: Uuid,
    pub name: String,
    #[sqlx(try_from = "i32")]
    pub periodicity: Periodicity,
    pub custom_days: Option<i32>,
    pub created_at: DateTime<Utc>,
    pub updated_at: Option<DateTime<Utc>>,
    pub device_id: Uuid,
}

/// Table `"MaintenanceInstances"`.
#[derive(Debug, Clone, FromRow)]
#[sqlx(rename_all = "PascalCase")]
pub struct MaintenanceInstance {
    pub id: Uuid,
    pub date: DateTime<Utc>,
    pub cost: Option<Decimal>,
    pub provider: Option<String>,
    pub notes: Option<String>,
    pub created_at: DateTime<Utc>,
    pub updated_at: Option<DateTime<Utc>>,
    pub maintenance_type_id: Uuid,
}

/// Table `"HouseMembers"`.
#[derive(Debug, Clone, FromRow)]
#[sqlx(rename_all = "PascalCase")]
pub struct HouseMember {
    pub id: Uuid,
    #[sqlx(try_from = "String")]
    pub role: HouseRole,
    pub can_log_maintenance: bool,
    pub created_at: DateTime<Utc>,
    pub updated_at: Option<DateTime<Utc>>,
    pub user_id: Uuid,
    pub house_id: Uuid,
    pub can_view_costs: bool,
}

/// Table `"Invitations"`.
#[derive(Debug, Clone, FromRow)]
#[sqlx(rename_all = "PascalCase")]
pub struct Invitation {
    pub id: Uuid,
    pub token: String,
    #[sqlx(try_from = "String")]
    pub role: HouseRole,
    #[sqlx(try_from = "String")]
    pub status: InvitationStatus,
    pub expires_at: DateTime<Utc>,
    pub created_at: DateTime<Utc>,
    pub accepted_at: Option<DateTime<Utc>>,
    pub revoked_at: Option<DateTime<Utc>>,
    pub house_id: Uuid,
    pub created_by_user_id: Uuid,
    pub accepted_by_user_id: Option<Uuid>,
}

/// Table `"RefreshTokens"`.
#[derive(Debug, Clone, FromRow)]
#[sqlx(rename_all = "PascalCase")]
pub struct RefreshToken {
    pub id: Uuid,
    pub user_id: Uuid,
    pub token: String,
    pub expires_at: DateTime<Utc>,
    pub created_at: DateTime<Utc>,
    pub created_by_ip: Option<String>,
    pub revoked_at: Option<DateTime<Utc>>,
    pub revoked_by_ip: Option<String>,
    pub replaced_by_token: Option<String>,
    pub reason_revoked: Option<String>,
    pub family_id: Uuid,
    pub remember_me: bool,
}

impl RefreshToken {
    /// `RefreshToken.IsExpired` du C#.
    pub fn is_expired(&self, now: DateTime<Utc>) -> bool {
        now >= self.expires_at
    }

    /// `RefreshToken.IsActive` du C# : ni révoqué, ni expiré.
    pub fn is_active(&self, now: DateTime<Utc>) -> bool {
        self.revoked_at.is_none() && !self.is_expired(now)
    }
}

/// Table `"ApiKeys"`.
#[derive(Debug, Clone, FromRow)]
#[sqlx(rename_all = "PascalCase")]
pub struct ApiKey {
    pub id: Uuid,
    pub user_id: Uuid,
    pub name: String,
    pub prefix: String,
    pub key_hash: String,
    #[sqlx(try_from = "String")]
    pub scope: ApiKeyScope,
    pub created_at: DateTime<Utc>,
    pub created_by_ip: Option<String>,
    pub last_used_at: Option<DateTime<Utc>>,
    pub revoked_at: Option<DateTime<Utc>>,
}

impl ApiKey {
    pub fn is_active(&self) -> bool {
        self.revoked_at.is_none()
    }
}

/// Table `"AuditLogs"`.
#[derive(Debug, Clone, FromRow)]
#[sqlx(rename_all = "PascalCase")]
pub struct AuditLog {
    pub id: Uuid,
    pub entity_type: String,
    pub entity_id: String,
    pub action: String,
    pub user_id: Option<Uuid>,
    pub username: Option<String>,
    pub timestamp: DateTime<Utc>,
    pub old_values: Option<String>,
    pub new_values: Option<String>,
    pub changed_properties: Option<String>,
    pub ip_address: Option<String>,
    pub user_agent: Option<String>,
    pub additional_data: Option<String>,
}
