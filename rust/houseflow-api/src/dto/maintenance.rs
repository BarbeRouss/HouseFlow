//! DTO des entretiens (`MaintenanceDtos.cs` + `LogMaintenanceRequest` de
//! `Contracts.g.cs`).

use chrono::{DateTime, Utc};
use rust_decimal::Decimal;
use serde::{Deserialize, Serialize};
use uuid::Uuid;

use crate::error::ValidationErrors;
use crate::models::Periodicity;
use crate::services::calculator::MaintenanceTypeWithStatus;
use crate::validation::{max_length, not_in_future, push_error, range, Validate};

/// Borne haute du `[Range(typeof(decimal), "0", "79228162514264337593543950335")]`
/// posé sur `LogMaintenanceRequest.Cost` : `decimal.MaxValue`.
const DECIMAL_MAX: &str = "79228162514264337593543950335";

/// `ErrorMessage` du `[StringLength(200, MinimumLength = 1)]` posé sur `Name`.
const NAME_LENGTH_MESSAGE: &str = "Name must be between 1 and 200 characters";

/// `CreateMaintenanceTypeRequestDto`.
#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CreateMaintenanceTypeRequest {
    pub name: Option<String>,
    /// Absent ⇒ `default(Periodicity)` = `Annual`, comme la liaison de modèle .NET.
    pub periodicity: Option<Periodicity>,
    pub custom_days: Option<i32>,
}

impl CreateMaintenanceTypeRequest {
    pub fn name(&self) -> &str {
        self.name.as_deref().unwrap_or_default()
    }

    pub fn periodicity(&self) -> Periodicity {
        self.periodicity.unwrap_or(Periodicity::Annual)
    }
}

impl Validate for CreateMaintenanceTypeRequest {
    fn validate(&self, errors: &mut ValidationErrors) {
        match self.name.as_ref() {
            Some(name) if !name.trim().is_empty() => {
                if name.chars().count() > 200 {
                    push_error(errors, "Name", NAME_LENGTH_MESSAGE.into());
                }
            }
            _ => push_error(errors, "Name", "Maintenance type name is required".into()),
        }
        validate_periodicity(errors, self.periodicity);
        validate_custom_days(errors, self.custom_days);
    }
}

/// `UpdateMaintenanceTypeRequestDto` : seuls les champs transmis sont appliqués.
#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct UpdateMaintenanceTypeRequest {
    pub name: Option<String>,
    pub periodicity: Option<Periodicity>,
    pub custom_days: Option<i32>,
}

impl Validate for UpdateMaintenanceTypeRequest {
    fn validate(&self, errors: &mut ValidationErrors) {
        if let Some(name) = self.name.as_deref() {
            if name.is_empty() || name.chars().count() > 200 {
                push_error(errors, "Name", NAME_LENGTH_MESSAGE.into());
            }
        }
        validate_periodicity(errors, self.periodicity);
        validate_custom_days(errors, self.custom_days);
    }
}

/// `[EnumDataType(typeof(Periodicity))]` : une valeur hors énumération est refusée.
fn validate_periodicity(errors: &mut ValidationErrors, periodicity: Option<Periodicity>) {
    if matches!(periodicity, Some(Periodicity::Unknown(_))) {
        push_error(errors, "Periodicity", "Invalid periodicity".into());
    }
}

/// `[Range(1, 3650)]` sur `CustomDays`.
fn validate_custom_days(errors: &mut ValidationErrors, custom_days: Option<i32>) {
    if let Some(days) = custom_days {
        range(
            errors,
            "CustomDays",
            days,
            1,
            3650,
            Some("Custom days must be between 1 and 3650 (10 years)"),
        );
    }
}

/// `MaintenanceTypeDto`.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct MaintenanceTypeDto {
    pub id: Uuid,
    pub name: String,
    pub periodicity: Periodicity,
    pub custom_days: Option<i32>,
    pub device_id: Uuid,
    pub created_at: DateTime<Utc>,
}

/// `MaintenanceTypeWithStatusDto`.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct MaintenanceTypeWithStatusDto {
    pub id: Uuid,
    pub name: String,
    pub periodicity: Periodicity,
    pub custom_days: Option<i32>,
    pub device_id: Uuid,
    pub created_at: DateTime<Utc>,
    /// `up_to_date`, `pending` ou `overdue`.
    pub status: String,
    pub last_maintenance_date: Option<DateTime<Utc>>,
    pub next_due_date: Option<DateTime<Utc>>,
}

impl From<MaintenanceTypeWithStatus> for MaintenanceTypeWithStatusDto {
    fn from(value: MaintenanceTypeWithStatus) -> Self {
        Self {
            id: value.id,
            name: value.name,
            periodicity: value.periodicity,
            custom_days: value.custom_days,
            device_id: value.device_id,
            created_at: value.created_at,
            status: value.status.to_string(),
            last_maintenance_date: value.last_maintenance_date,
            next_due_date: value.next_due_date,
        }
    }
}

/// `HouseFlow.Contracts.LogMaintenanceRequest`.
#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct LogMaintenanceRequest {
    #[serde(
        default,
        deserialize_with = "crate::dto::datetime::deserialize_optional"
    )]
    pub date: Option<DateTime<Utc>>,
    pub cost: Option<Decimal>,
    pub provider: Option<String>,
    pub notes: Option<String>,
}

impl Validate for LogMaintenanceRequest {
    fn validate(&self, errors: &mut ValidationErrors) {
        if self.date.is_none() {
            push_error(errors, "Date", "The Date field is required.".into());
        }
        validate_cost(errors, self.cost, None);
        validate_provider_and_notes(
            errors,
            self.provider.as_deref(),
            self.notes.as_deref(),
            None,
        );
    }
}

/// `UpdateMaintenanceInstanceRequestDto` : porte son propre `[NotInFuture]`, la date
/// future est donc refusée par la validation avant même d'atteindre le service.
#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct UpdateMaintenanceInstanceRequest {
    #[serde(
        default,
        deserialize_with = "crate::dto::datetime::deserialize_optional"
    )]
    pub date: Option<DateTime<Utc>>,
    pub cost: Option<Decimal>,
    pub provider: Option<String>,
    pub notes: Option<String>,
}

impl Validate for UpdateMaintenanceInstanceRequest {
    fn validate(&self, errors: &mut ValidationErrors) {
        // `[NotInFuture(ErrorMessage = "Maintenance date cannot be in the future")]`.
        if !not_in_future(self.date) {
            push_error(
                errors,
                "Date",
                "Maintenance date cannot be in the future".into(),
            );
        }
        validate_cost(
            errors,
            self.cost,
            Some(("1000000", "Cost must be between 0 and 1,000,000")),
        );
        validate_provider_and_notes(
            errors,
            self.provider.as_deref(),
            self.notes.as_deref(),
            Some((
                "Provider name cannot exceed 200 characters",
                "Notes cannot exceed 2000 characters",
            )),
        );
    }
}

/// `[Range]` sur `Cost` : borne haute et message diffèrent entre les deux requêtes.
fn validate_cost(
    errors: &mut ValidationErrors,
    cost: Option<Decimal>,
    bounds: Option<(&str, &str)>,
) {
    let Some(cost) = cost else { return };
    let (max, message) = bounds.unwrap_or((DECIMAL_MAX, ""));
    let max: Decimal = max.parse().expect("bound is a valid decimal");
    range(
        errors,
        "Cost",
        cost,
        Decimal::ZERO,
        max,
        (!message.is_empty()).then_some(message),
    );
}

/// `[StringLength]` sur `Provider` (200) et `Notes` (2000). Les bornes sont les mêmes
/// dans les deux requêtes, les messages non : `messages` porte ceux de
/// `UpdateMaintenanceInstanceRequestDto`, `None` laisse ceux d'ASP.NET.
fn validate_provider_and_notes(
    errors: &mut ValidationErrors,
    provider: Option<&str>,
    notes: Option<&str>,
    messages: Option<(&str, &str)>,
) {
    fn bounded(
        errors: &mut ValidationErrors,
        field: &str,
        value: &str,
        max: usize,
        message: Option<&str>,
    ) {
        match message {
            Some(message) if value.chars().count() > max => {
                push_error(errors, field, message.to_string())
            }
            Some(_) => {}
            None => max_length(errors, field, value, max),
        }
    }

    if let Some(provider) = provider {
        bounded(errors, "Provider", provider, 200, messages.map(|(p, _)| p));
    }
    if let Some(notes) = notes {
        bounded(errors, "Notes", notes, 2000, messages.map(|(_, n)| n));
    }
}

/// `MaintenanceInstanceDto`.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct MaintenanceInstanceDto {
    pub id: Uuid,
    pub date: DateTime<Utc>,
    pub cost: Option<Decimal>,
    pub provider: Option<String>,
    pub notes: Option<String>,
    pub maintenance_type_id: Uuid,
    pub maintenance_type_name: String,
    pub created_at: DateTime<Utc>,
}

/// `MaintenanceHistoryResponseDto`.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct MaintenanceHistoryResponse {
    pub instances: Vec<MaintenanceInstanceDto>,
    pub total_spent: Decimal,
    pub count: i32,
}

/// `UpcomingTaskDto`.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct UpcomingTaskDto {
    pub maintenance_type_id: Uuid,
    pub maintenance_type_name: String,
    pub device_id: Uuid,
    pub device_name: String,
    pub device_type: String,
    pub house_id: Uuid,
    pub house_name: String,
    /// `pending` ou `overdue` — les tâches à jour ne sont pas listées.
    pub status: String,
    pub next_due_date: Option<DateTime<Utc>>,
    pub last_maintenance_date: Option<DateTime<Utc>>,
    /// Nom de la périodicité (`mt.Periodicity.ToString()` du C#).
    pub periodicity: String,
}

/// `UpcomingTasksResponseDto`.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct UpcomingTasksResponse {
    pub tasks: Vec<UpcomingTaskDto>,
    pub overdue_count: i32,
    pub pending_count: i32,
}

#[cfg(test)]
mod tests {
    use super::*;
    use chrono::Duration;

    fn errors_of<T: Validate>(request: &T) -> ValidationErrors {
        let mut errors = ValidationErrors::new();
        request.validate(&mut errors);
        errors
    }

    #[test]
    fn creating_a_type_requires_a_name_with_the_dto_message() {
        let request = CreateMaintenanceTypeRequest {
            name: Some(String::new()),
            periodicity: Some(Periodicity::Annual),
            custom_days: None,
        };
        assert_eq!(
            errors_of(&request)["Name"],
            vec!["Maintenance type name is required"]
        );
    }

    #[test]
    fn an_absent_periodicity_defaults_to_annual() {
        let request: CreateMaintenanceTypeRequest =
            serde_json::from_str(r#"{"name":"Revision"}"#).unwrap();
        assert_eq!(request.periodicity(), Periodicity::Annual);
        assert!(errors_of(&request).is_empty());
    }

    #[test]
    fn a_periodicity_outside_the_enum_is_rejected() {
        let request: CreateMaintenanceTypeRequest =
            serde_json::from_str(r#"{"name":"Revision","periodicity":42}"#).unwrap();
        assert_eq!(
            errors_of(&request)["Periodicity"],
            vec!["Invalid periodicity"]
        );
    }

    #[test]
    fn custom_days_are_bounded_to_ten_years() {
        let request = CreateMaintenanceTypeRequest {
            name: Some("Revision".into()),
            periodicity: Some(Periodicity::Custom),
            custom_days: Some(4000),
        };
        assert_eq!(
            errors_of(&request)["CustomDays"],
            vec!["Custom days must be between 1 and 3650 (10 years)"]
        );
    }

    #[test]
    fn logging_a_maintenance_requires_a_date_and_a_positive_cost() {
        let missing: LogMaintenanceRequest = serde_json::from_str(r#"{"cost":10}"#).unwrap();
        assert!(errors_of(&missing).contains_key("Date"));

        let negative = LogMaintenanceRequest {
            date: Some(Utc::now()),
            cost: Some(Decimal::from(-1)),
            provider: None,
            notes: None,
        };
        assert!(errors_of(&negative).contains_key("Cost"));
    }

    #[test]
    fn logging_a_maintenance_accepts_a_bare_date() {
        let request: LogMaintenanceRequest =
            serde_json::from_str(r#"{"date":"2026-01-05","cost":150.5}"#).unwrap();
        assert_eq!(
            request.date.unwrap().to_rfc3339(),
            "2026-01-05T00:00:00+00:00"
        );
        assert!(errors_of(&request).is_empty());
    }

    #[test]
    fn updating_an_instance_refuses_a_future_date_before_reaching_the_service() {
        let request = UpdateMaintenanceInstanceRequest {
            date: Some(Utc::now() + Duration::days(30)),
            cost: None,
            provider: None,
            notes: None,
        };
        assert_eq!(
            errors_of(&request)["Date"],
            vec!["Maintenance date cannot be in the future"]
        );
    }

    #[test]
    fn updating_an_instance_bounds_the_cost_to_a_million() {
        let request = UpdateMaintenanceInstanceRequest {
            date: None,
            cost: Some(Decimal::from(1_000_001)),
            provider: None,
            notes: None,
        };
        assert_eq!(
            errors_of(&request)["Cost"],
            vec!["Cost must be between 0 and 1,000,000"]
        );
    }
}
