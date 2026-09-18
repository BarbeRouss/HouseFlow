//! DTO de gestion des clés API (`ApiKeyDtos.cs`).

use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};
use uuid::Uuid;

use crate::error::ValidationErrors;
use crate::models::ApiKey;
use crate::validation::{required, string_length, Validate};

/// `CreateApiKeyRequestDto` — `Scope` vaut `ReadWrite` par défaut.
#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CreateApiKeyRequest {
    pub name: Option<String>,
    pub scope: Option<String>,
}

impl CreateApiKeyRequest {
    pub fn name(&self) -> &str {
        self.name.as_deref().unwrap_or_default()
    }

    pub fn scope(&self) -> &str {
        self.scope.as_deref().unwrap_or("ReadWrite")
    }
}

impl Validate for CreateApiKeyRequest {
    fn validate(&self, errors: &mut ValidationErrors) {
        match self.name.as_ref() {
            Some(name) if !name.trim().is_empty() => {
                string_length(errors, "Name", name, 1, 100);
                if let Some(messages) = errors.get_mut("Name") {
                    // Message personnalisé du DTO C#.
                    messages.clear();
                    messages.push("Name must be between 1 and 100 characters".to_string());
                }
            }
            _ => {
                required(errors, "Name", None);
                if let Some(messages) = errors.get_mut("Name") {
                    messages.clear();
                    messages.push("Name is required".to_string());
                }
            }
        }
    }
}

/// `CreateApiKeyResponseDto` — seule réponse où la clé en clair apparaît.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct CreateApiKeyResponse {
    pub id: Uuid,
    pub name: String,
    pub key: String,
    pub prefix: String,
    pub scope: String,
    pub created_at: DateTime<Utc>,
}

/// `ApiKeyDto`.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ApiKeyDto {
    pub id: Uuid,
    pub name: String,
    pub prefix: String,
    pub scope: String,
    pub created_at: DateTime<Utc>,
    pub last_used_at: Option<DateTime<Utc>>,
}

impl From<&ApiKey> for ApiKeyDto {
    fn from(key: &ApiKey) -> Self {
        Self {
            id: key.id,
            name: key.name.clone(),
            prefix: key.prefix.clone(),
            scope: key.scope.to_string(),
            created_at: key.created_at,
            last_used_at: key.last_used_at,
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn errors_of(request: &CreateApiKeyRequest) -> ValidationErrors {
        let mut errors = ValidationErrors::new();
        request.validate(&mut errors);
        errors
    }

    #[test]
    fn name_is_required_with_the_custom_message() {
        let errors = errors_of(&CreateApiKeyRequest {
            name: None,
            scope: None,
        });
        assert_eq!(errors["Name"], vec!["Name is required"]);
    }

    #[test]
    fn name_longer_than_100_characters_is_rejected() {
        let errors = errors_of(&CreateApiKeyRequest {
            name: Some("x".repeat(101)),
            scope: None,
        });
        assert_eq!(
            errors["Name"],
            vec!["Name must be between 1 and 100 characters"]
        );
    }

    #[test]
    fn scope_defaults_to_read_write() {
        let request = CreateApiKeyRequest {
            name: Some("key".into()),
            scope: None,
        };
        assert_eq!(request.scope(), "ReadWrite");
        assert!(errors_of(&request).is_empty());
    }
}
