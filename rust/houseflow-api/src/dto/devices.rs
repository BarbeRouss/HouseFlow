//! DTO des appareils (`DeviceDtos.cs` + `CreateDeviceRequest` / `UpdateDeviceRequest`
//! de `Contracts.g.cs`).

use chrono::{DateTime, Utc};
use rust_decimal::Decimal;
use serde::{Deserialize, Serialize};
use uuid::Uuid;

use crate::dto::maintenance::MaintenanceTypeWithStatusDto;
use crate::error::ValidationErrors;
use crate::models::Device;
use crate::validation::{max_length, required, string_length, Validate};

/// `HouseFlow.Contracts.CreateDeviceRequest`.
#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CreateDeviceRequest {
    pub name: Option<String>,
    #[serde(rename = "type")]
    pub device_type: Option<String>,
    pub brand: Option<String>,
    pub model: Option<String>,
    #[serde(
        default,
        deserialize_with = "crate::dto::datetime::deserialize_optional"
    )]
    pub install_date: Option<DateTime<Utc>>,
}

impl CreateDeviceRequest {
    pub fn name(&self) -> &str {
        self.name.as_deref().unwrap_or_default()
    }

    pub fn device_type(&self) -> &str {
        self.device_type.as_deref().unwrap_or_default()
    }
}

impl Validate for CreateDeviceRequest {
    fn validate(&self, errors: &mut ValidationErrors) {
        if required(errors, "Name", self.name.as_ref()) {
            string_length(errors, "Name", self.name(), 1, 200);
        }
        required(errors, "Type", self.device_type.as_ref());
        validate_optional_identity(errors, self.brand.as_deref(), self.model.as_deref());
    }
}

/// `HouseFlow.Contracts.UpdateDeviceRequest` : seuls les champs transmis sont appliqués.
#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct UpdateDeviceRequest {
    pub name: Option<String>,
    #[serde(rename = "type")]
    pub device_type: Option<String>,
    pub brand: Option<String>,
    pub model: Option<String>,
    #[serde(
        default,
        deserialize_with = "crate::dto::datetime::deserialize_optional"
    )]
    pub install_date: Option<DateTime<Utc>>,
}

impl Validate for UpdateDeviceRequest {
    fn validate(&self, errors: &mut ValidationErrors) {
        if let Some(name) = self.name.as_deref() {
            string_length(errors, "Name", name, 1, 200);
        }
        validate_optional_identity(errors, self.brand.as_deref(), self.model.as_deref());
    }
}

/// Bornes communes : `Brand` et `Model` sont limités à 200 caractères.
fn validate_optional_identity(
    errors: &mut ValidationErrors,
    brand: Option<&str>,
    model: Option<&str>,
) {
    if let Some(brand) = brand {
        max_length(errors, "Brand", brand, 200);
    }
    if let Some(model) = model {
        max_length(errors, "Model", model, 200);
    }
}

/// `DeviceDto`.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DeviceDto {
    pub id: Uuid,
    pub name: String,
    #[serde(rename = "type")]
    pub device_type: String,
    pub brand: Option<String>,
    pub model: Option<String>,
    pub install_date: Option<DateTime<Utc>>,
    pub house_id: Uuid,
    pub created_at: DateTime<Utc>,
}

impl From<&Device> for DeviceDto {
    fn from(device: &Device) -> Self {
        Self {
            id: device.id,
            name: device.name.clone(),
            device_type: device.device_type.clone(),
            brand: device.brand.clone(),
            model: device.model.clone(),
            install_date: device.install_date,
            house_id: device.house_id,
            created_at: device.created_at,
        }
    }
}

/// `DeviceSummaryDto`.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DeviceSummaryDto {
    pub id: Uuid,
    pub name: String,
    #[serde(rename = "type")]
    pub device_type: String,
    pub brand: Option<String>,
    pub model: Option<String>,
    pub install_date: Option<DateTime<Utc>>,
    pub house_id: Uuid,
    pub created_at: DateTime<Utc>,
    pub score: i32,
    /// `up_to_date`, `pending` ou `overdue`.
    pub status: String,
    pub pending_count: i32,
    pub maintenance_types_count: i32,
}

/// `DeviceDetailDto`.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DeviceDetailDto {
    pub id: Uuid,
    pub name: String,
    #[serde(rename = "type")]
    pub device_type: String,
    pub brand: Option<String>,
    pub model: Option<String>,
    pub install_date: Option<DateTime<Utc>>,
    pub house_id: Uuid,
    pub created_at: DateTime<Utc>,
    pub score: i32,
    pub status: String,
    pub pending_count: i32,
    pub maintenance_types_count: i32,
    pub maintenance_types: Vec<MaintenanceTypeWithStatusDto>,
    /// `decimal` de .NET : nombre JSON, jamais chaîne (cf. `rust/PORTING.md`).
    #[serde(serialize_with = "rust_decimal::serde::float::serialize")]
    pub total_spent: Decimal,
    pub maintenance_count: i32,
}

#[cfg(test)]
mod tests {
    use super::*;

    fn errors_of<T: Validate>(request: &T) -> ValidationErrors {
        let mut errors = ValidationErrors::new();
        request.validate(&mut errors);
        errors
    }

    fn create(name: Option<&str>, device_type: Option<&str>) -> CreateDeviceRequest {
        CreateDeviceRequest {
            name: name.map(str::to_string),
            device_type: device_type.map(str::to_string),
            brand: None,
            model: None,
            install_date: None,
        }
    }

    #[test]
    fn create_requires_a_name_and_a_type() {
        assert!(errors_of(&create(Some(""), Some("Chaudiere"))).contains_key("Name"));
        assert!(errors_of(&create(Some("Chaudiere"), Some(""))).contains_key("Type"));
        assert!(errors_of(&create(Some("Chaudiere"), Some("heating"))).is_empty());
    }

    #[test]
    fn the_type_field_is_read_from_the_json_key_type() {
        let request: CreateDeviceRequest =
            serde_json::from_str(r#"{"name":"Chaudiere","type":"heating"}"#).unwrap();
        assert_eq!(request.device_type(), "heating");
        assert!(request.install_date.is_none());
    }

    #[test]
    fn a_bare_install_date_is_accepted_like_the_generated_contract_writes_it() {
        let request: CreateDeviceRequest =
            serde_json::from_str(r#"{"name":"C","type":"t","installDate":"2024-03-15"}"#).unwrap();
        assert_eq!(
            request.install_date.unwrap().to_rfc3339(),
            "2024-03-15T00:00:00+00:00"
        );
    }

    #[test]
    fn update_only_bounds_the_fields_that_are_present() {
        let request = UpdateDeviceRequest {
            name: None,
            device_type: None,
            brand: Some("x".repeat(201)),
            model: None,
            install_date: None,
        };
        let errors = errors_of(&request);
        assert!(errors.contains_key("Brand"));
        assert!(!errors.contains_key("Name"));
    }
}
