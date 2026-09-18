//! DTO des maisons (`HouseDtos.cs` + `CreateHouseRequest` / `UpdateHouseRequest`
//! de `Contracts.g.cs`).

use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};
use uuid::Uuid;

use crate::dto::devices::DeviceSummaryDto;
use crate::error::ValidationErrors;
use crate::models::House;
use crate::validation::{max_length, required, string_length, Validate};

/// `HouseFlow.Contracts.CreateHouseRequest`.
#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CreateHouseRequest {
    pub name: Option<String>,
    pub address: Option<String>,
    pub zip_code: Option<String>,
    pub city: Option<String>,
}

impl CreateHouseRequest {
    pub fn name(&self) -> &str {
        self.name.as_deref().unwrap_or_default()
    }
}

impl Validate for CreateHouseRequest {
    fn validate(&self, errors: &mut ValidationErrors) {
        if required(errors, "Name", self.name.as_ref()) {
            string_length(
                errors,
                "Name",
                self.name.as_deref().unwrap_or_default(),
                1,
                200,
            );
        }
        validate_optional_address(
            errors,
            self.address.as_deref(),
            self.zip_code.as_deref(),
            self.city.as_deref(),
        );
    }
}

/// `HouseFlow.Contracts.UpdateHouseRequest` : tous les champs sont optionnels, seuls
/// ceux transmis (non `null`) sont appliqués.
#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct UpdateHouseRequest {
    pub name: Option<String>,
    pub address: Option<String>,
    pub zip_code: Option<String>,
    pub city: Option<String>,
}

impl Validate for UpdateHouseRequest {
    fn validate(&self, errors: &mut ValidationErrors) {
        // `[StringLength]` ignore `null` mais pas la chaîne vide (longueur minimale 1).
        if let Some(name) = self.name.as_deref() {
            string_length(errors, "Name", name, 1, 200);
        }
        validate_optional_address(
            errors,
            self.address.as_deref(),
            self.zip_code.as_deref(),
            self.city.as_deref(),
        );
    }
}

/// Bornes communes aux deux requêtes : `Address` 500, `ZipCode` 20, `City` 200.
fn validate_optional_address(
    errors: &mut ValidationErrors,
    address: Option<&str>,
    zip_code: Option<&str>,
    city: Option<&str>,
) {
    if let Some(address) = address {
        max_length(errors, "Address", address, 500);
    }
    if let Some(zip_code) = zip_code {
        max_length(errors, "ZipCode", zip_code, 20);
    }
    if let Some(city) = city {
        max_length(errors, "City", city, 200);
    }
}

/// `HouseDto`.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct HouseDto {
    pub id: Uuid,
    pub name: String,
    pub address: Option<String>,
    pub zip_code: Option<String>,
    pub city: Option<String>,
    pub created_at: DateTime<Utc>,
}

impl From<&House> for HouseDto {
    fn from(house: &House) -> Self {
        Self {
            id: house.id,
            name: house.name.clone(),
            address: house.address.clone(),
            zip_code: house.zip_code.clone(),
            city: house.city.clone(),
            created_at: house.created_at,
        }
    }
}

/// `HouseSummaryDto`.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct HouseSummaryDto {
    pub id: Uuid,
    pub name: String,
    pub address: Option<String>,
    pub zip_code: Option<String>,
    pub city: Option<String>,
    pub created_at: DateTime<Utc>,
    pub score: i32,
    pub devices_count: i32,
    pub pending_count: i32,
    pub overdue_count: i32,
    pub user_role: Option<String>,
}

/// `HousesListResponseDto`.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct HousesListResponse {
    pub houses: Vec<HouseSummaryDto>,
    pub global_score: i32,
}

/// `HouseDetailDto`.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct HouseDetailDto {
    pub id: Uuid,
    pub name: String,
    pub address: Option<String>,
    pub zip_code: Option<String>,
    pub city: Option<String>,
    pub created_at: DateTime<Utc>,
    pub score: i32,
    pub devices_count: i32,
    pub pending_count: i32,
    pub overdue_count: i32,
    pub devices: Vec<DeviceSummaryDto>,
    pub user_role: Option<String>,
}

#[cfg(test)]
mod tests {
    use super::*;

    fn errors_of<T: Validate>(request: &T) -> ValidationErrors {
        let mut errors = ValidationErrors::new();
        request.validate(&mut errors);
        errors
    }

    fn create(name: Option<&str>) -> CreateHouseRequest {
        CreateHouseRequest {
            name: name.map(str::to_string),
            address: None,
            zip_code: None,
            city: None,
        }
    }

    #[test]
    fn create_requires_a_name() {
        assert!(errors_of(&create(None)).contains_key("Name"));
        assert!(errors_of(&create(Some(""))).contains_key("Name"));
        assert!(errors_of(&create(Some("Ma Maison"))).is_empty());
    }

    #[test]
    fn create_rejects_a_name_longer_than_two_hundred_characters() {
        assert!(errors_of(&create(Some(&"x".repeat(201)))).contains_key("Name"));
    }

    #[test]
    fn create_bounds_the_optional_address_fields() {
        let request = CreateHouseRequest {
            name: Some("Ma Maison".into()),
            address: Some("x".repeat(501)),
            zip_code: Some("x".repeat(21)),
            city: Some("x".repeat(201)),
        };
        let errors = errors_of(&request);
        assert!(errors.contains_key("Address"));
        assert!(errors.contains_key("ZipCode"));
        assert!(errors.contains_key("City"));
    }

    #[test]
    fn update_accepts_absent_fields_but_not_an_empty_name() {
        let absent = UpdateHouseRequest {
            name: None,
            address: None,
            zip_code: None,
            city: None,
        };
        assert!(errors_of(&absent).is_empty());

        let empty_name = UpdateHouseRequest {
            name: Some(String::new()),
            address: None,
            zip_code: None,
            city: None,
        };
        assert!(errors_of(&empty_name).contains_key("Name"));
    }
}
