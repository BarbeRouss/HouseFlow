//! DTO des préférences utilisateur (`UserSettingsDtos.cs`).

use serde::{Deserialize, Serialize};

use crate::error::ValidationErrors;
use crate::validation::{regular_expression, required, Validate};

/// `UserSettingsDto`.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct UserSettings {
    pub theme: String,
    pub language: String,
}

/// `UpdateUserSettingsDto`.
#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct UpdateUserSettings {
    pub theme: Option<String>,
    pub language: Option<String>,
}

const THEME_PATTERN: &str = "^(light|dark|system)$";
const LANGUAGE_PATTERN: &str = "^(fr|en)$";

impl UpdateUserSettings {
    pub fn theme(&self) -> &str {
        self.theme.as_deref().unwrap_or_default()
    }

    pub fn language(&self) -> &str {
        self.language.as_deref().unwrap_or_default()
    }
}

impl Validate for UpdateUserSettings {
    fn validate(&self, errors: &mut ValidationErrors) {
        if required(errors, "Theme", self.theme.as_ref()) {
            let theme = self.theme.as_ref().unwrap();
            regular_expression(
                errors,
                "Theme",
                THEME_PATTERN,
                matches!(theme.as_str(), "light" | "dark" | "system"),
                Some("Theme must be 'light', 'dark', or 'system'"),
            );
        }
        if required(errors, "Language", self.language.as_ref()) {
            let language = self.language.as_ref().unwrap();
            regular_expression(
                errors,
                "Language",
                LANGUAGE_PATTERN,
                matches!(language.as_str(), "fr" | "en"),
                Some("Language must be 'fr' or 'en'"),
            );
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn errors_of(theme: Option<&str>, language: Option<&str>) -> ValidationErrors {
        let request = UpdateUserSettings {
            theme: theme.map(str::to_string),
            language: language.map(str::to_string),
        };
        let mut errors = ValidationErrors::new();
        request.validate(&mut errors);
        errors
    }

    #[test]
    fn accepted_values_pass() {
        assert!(errors_of(Some("dark"), Some("en")).is_empty());
        assert!(errors_of(Some("system"), Some("fr")).is_empty());
    }

    #[test]
    fn unknown_theme_or_language_is_rejected_with_the_dto_message() {
        let errors = errors_of(Some("neon"), Some("de"));
        assert_eq!(
            errors["Theme"],
            vec!["Theme must be 'light', 'dark', or 'system'"]
        );
        assert_eq!(errors["Language"], vec!["Language must be 'fr' or 'en'"]);
    }

    #[test]
    fn missing_values_are_required() {
        let errors = errors_of(None, None);
        assert!(errors.contains_key("Theme"));
        assert!(errors.contains_key("Language"));
    }
}
