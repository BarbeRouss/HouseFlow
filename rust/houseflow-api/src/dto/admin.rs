//! DTO d'administration (`AdminDtos.cs` + `SetUserAdminRequest` de `Contracts.g.cs`).

use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};
use uuid::Uuid;

use crate::error::ValidationErrors;
use crate::validation::Validate;

/// `AdminStatsDto` — compteurs de la plateforme.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct AdminStatsDto {
    pub users: i32,
    pub admins: i32,
    pub houses: i32,
    pub devices: i32,
    pub maintenance_instances: i32,
}

/// `AdminUserDto`.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct AdminUserDto {
    pub id: Uuid,
    pub email: String,
    pub first_name: String,
    pub last_name: String,
    pub is_admin: bool,
    pub created_at: DateTime<Utc>,
    pub houses_count: i32,
}

/// `AdminUsersPageDto`.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct AdminUsersPageDto {
    pub users: Vec<AdminUserDto>,
    pub total: i32,
    pub page: i32,
    pub page_size: i32,
}

/// `SetUserAdminRequest` — aucune annotation de validation ; un corps sans `isAdmin`
/// vaut `false`, comme le constructeur JSON généré par NSwag.
#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SetUserAdminRequest {
    pub is_admin: Option<bool>,
}

impl SetUserAdminRequest {
    pub fn is_admin(&self) -> bool {
        self.is_admin.unwrap_or(false)
    }
}

impl Validate for SetUserAdminRequest {
    fn validate(&self, _errors: &mut ValidationErrors) {}
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn a_missing_flag_means_false() {
        let request: SetUserAdminRequest = serde_json::from_str("{}").unwrap();
        assert!(!request.is_admin());
    }

    #[test]
    fn the_flag_is_read_in_camel_case() {
        let request: SetUserAdminRequest = serde_json::from_str(r#"{"isAdmin":true}"#).unwrap();
        assert!(request.is_admin());
    }

    #[test]
    fn the_users_page_is_serialized_in_camel_case() {
        let json = serde_json::to_value(AdminUsersPageDto {
            users: vec![],
            total: 3,
            page: 1,
            page_size: 20,
        })
        .unwrap();
        assert_eq!(json["pageSize"], 20);
        assert_eq!(json["total"], 3);
    }
}
