//! DTO des membres et des invitations (`MemberDtos.cs`).

use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};
use uuid::Uuid;

use crate::error::ValidationErrors;
use crate::validation::Validate;

/// `HouseMemberDto`.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct HouseMemberDto {
    pub id: Uuid,
    pub user_id: Uuid,
    pub first_name: String,
    pub last_name: String,
    pub email: String,
    pub role: String,
    pub can_log_maintenance: bool,
    pub can_view_costs: bool,
    pub created_at: DateTime<Utc>,
}

/// `UpdateMemberRoleRequestDto` — `[Required(ErrorMessage = "Role is required")]`.
#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct UpdateMemberRoleRequest {
    pub role: Option<String>,
}

impl Validate for UpdateMemberRoleRequest {
    fn validate(&self, errors: &mut ValidationErrors) {
        required_role(errors, self.role.as_deref());
    }
}

/// `CreateInvitationRequestDto` — même contrainte que ci-dessus.
#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CreateInvitationRequest {
    pub role: Option<String>,
}

impl Validate for CreateInvitationRequest {
    fn validate(&self, errors: &mut ValidationErrors) {
        required_role(errors, self.role.as_deref());
    }
}

/// `[Required(ErrorMessage = "Role is required")]` : message personnalisé du DTO C#.
fn required_role(errors: &mut ValidationErrors, role: Option<&str>) {
    if role.is_none_or(|value| value.trim().is_empty()) {
        errors
            .entry("Role".to_string())
            .or_default()
            .push("Role is required".to_string());
    }
}

/// `UpdateMemberPermissionsRequestDto` — deux permissions optionnelles : une valeur
/// absente laisse la permission inchangée.
#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct UpdateMemberPermissionsRequest {
    pub can_log_maintenance: Option<bool>,
    pub can_view_costs: Option<bool>,
}

impl Validate for UpdateMemberPermissionsRequest {
    fn validate(&self, _errors: &mut ValidationErrors) {}
}

/// `InvitationDto` — le jeton est masqué (chaîne vide) pour un appelant non
/// propriétaire, comme `ToInvitationDto(redactToken: true)`.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct InvitationDto {
    pub id: Uuid,
    pub token: String,
    pub role: String,
    pub status: String,
    pub house_id: Uuid,
    pub house_name: String,
    pub created_by_name: String,
    pub expires_at: DateTime<Utc>,
    pub created_at: DateTime<Utc>,
}

/// `InvitationInfoDto` — réponse du point d'entrée anonyme, volontairement pauvre.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct InvitationInfoDto {
    pub id: Uuid,
    pub house_name: String,
    pub role: String,
    pub invited_by_name: String,
    pub expires_at: DateTime<Utc>,
    pub is_expired: bool,
}

/// `AcceptInvitationResponseDto`.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct AcceptInvitationResponse {
    pub house_id: Uuid,
    pub house_name: String,
    pub role: String,
}

/// `HouseCollaboratorsDto`.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct HouseCollaboratorsDto {
    pub house_id: Uuid,
    pub house_name: String,
    pub members: Vec<HouseMemberDto>,
    pub pending_invitations: Vec<InvitationDto>,
}

/// `AllCollaboratorsResponseDto`.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct AllCollaboratorsResponse {
    pub houses: Vec<HouseCollaboratorsDto>,
}

#[cfg(test)]
mod tests {
    use super::*;

    fn errors_of(request: &UpdateMemberRoleRequest) -> ValidationErrors {
        let mut errors = ValidationErrors::new();
        request.validate(&mut errors);
        errors
    }

    #[test]
    fn role_is_required_with_the_custom_message() {
        assert_eq!(
            errors_of(&UpdateMemberRoleRequest { role: None })["Role"],
            vec!["Role is required"]
        );
        assert_eq!(
            errors_of(&UpdateMemberRoleRequest {
                role: Some("  ".into())
            })["Role"],
            vec!["Role is required"]
        );
    }

    #[test]
    fn a_role_that_is_present_passes_model_validation() {
        // La valeur elle-même est vérifiée par le contrôleur, pas par les annotations.
        assert!(errors_of(&UpdateMemberRoleRequest {
            role: Some("Nimportequoi".into())
        })
        .is_empty());
    }

    #[test]
    fn permissions_default_to_leaving_everything_unchanged() {
        let request: UpdateMemberPermissionsRequest = serde_json::from_str("{}").unwrap();
        assert!(request.can_log_maintenance.is_none());
        assert!(request.can_view_costs.is_none());
    }

    #[test]
    fn member_dto_is_serialized_in_camel_case() {
        let json = serde_json::to_value(HouseMemberDto {
            id: Uuid::nil(),
            user_id: Uuid::nil(),
            first_name: "Ada".into(),
            last_name: "Lovelace".into(),
            email: "ada@example.com".into(),
            role: "Tenant".into(),
            can_log_maintenance: true,
            can_view_costs: false,
            created_at: Utc::now(),
        })
        .unwrap();
        assert_eq!(json["firstName"], "Ada");
        assert_eq!(json["canLogMaintenance"], true);
        assert_eq!(json["canViewCosts"], false);
    }
}
