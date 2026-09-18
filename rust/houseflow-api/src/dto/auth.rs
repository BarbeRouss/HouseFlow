//! DTO d'authentification (`RegisterRequest`, `LoginRequest`, `AuthResponse`, `User`).

use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};
use uuid::Uuid;

use crate::error::ValidationErrors;
use crate::models::User;
use crate::validation::{
    email_format, is_valid_email, min_length, password_matches_policy, regular_expression,
    required, string_length, Validate,
};

/// `HouseFlow.Contracts.RegisterRequest`.
///
/// Les champs sont optionnels côté désérialisation pour que l'absence produise la
/// même 400 `[Required]` qu'ASP.NET plutôt qu'une erreur de parsing JSON.
#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct RegisterRequest {
    pub first_name: Option<String>,
    pub last_name: Option<String>,
    pub email: Option<String>,
    pub password: Option<String>,
}

const PASSWORD_PATTERN: &str = r"^(?=.*\d).{8,}$";

impl Validate for RegisterRequest {
    fn validate(&self, errors: &mut ValidationErrors) {
        if required(errors, "FirstName", self.first_name.as_ref()) {
            string_length(
                errors,
                "FirstName",
                self.first_name.as_ref().unwrap(),
                1,
                100,
            );
        }
        if required(errors, "LastName", self.last_name.as_ref()) {
            string_length(errors, "LastName", self.last_name.as_ref().unwrap(), 1, 100);
        }
        let email_ok = required(errors, "Email", self.email.as_ref());
        if required(errors, "Password", self.password.as_ref()) {
            let password = self.password.as_ref().unwrap();
            min_length(errors, "Password", password, 8);
            regular_expression(
                errors,
                "Password",
                PASSWORD_PATTERN,
                password_matches_policy(password),
                None,
            );
        }

        // IValidatableObject ne s'exécute qu'après les validations de propriété.
        if email_ok && errors.is_empty() {
            email_format(errors, "Email", self.email.as_ref().unwrap());
        }
    }
}

impl RegisterRequest {
    pub fn email(&self) -> &str {
        self.email.as_deref().unwrap_or_default()
    }

    pub fn first_name(&self) -> &str {
        self.first_name.as_deref().unwrap_or_default()
    }

    pub fn last_name(&self) -> &str {
        self.last_name.as_deref().unwrap_or_default()
    }

    pub fn password(&self) -> &str {
        self.password.as_deref().unwrap_or_default()
    }
}

/// `HouseFlow.Contracts.LoginRequest`.
#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct LoginRequest {
    pub email: Option<String>,
    pub password: Option<String>,
    pub remember_me: Option<bool>,
}

impl Validate for LoginRequest {
    fn validate(&self, errors: &mut ValidationErrors) {
        let email_ok = required(errors, "Email", self.email.as_ref());
        required(errors, "Password", self.password.as_ref());

        if email_ok && errors.is_empty() {
            let email = self.email.as_ref().unwrap();
            if !is_valid_email(email) {
                email_format(errors, "Email", email);
            }
        }
    }
}

impl LoginRequest {
    pub fn email(&self) -> &str {
        self.email.as_deref().unwrap_or_default()
    }

    pub fn password(&self) -> &str {
        self.password.as_deref().unwrap_or_default()
    }

    pub fn remember_me(&self) -> bool {
        self.remember_me.unwrap_or(false)
    }
}

/// `UserDto` — l'utilisateur tel qu'exposé par l'API.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct UserDto {
    pub id: Uuid,
    pub first_name: String,
    pub last_name: String,
    pub email: String,
    pub theme: String,
    pub language: String,
    pub is_admin: bool,
}

impl From<&User> for UserDto {
    fn from(user: &User) -> Self {
        Self {
            id: user.id,
            first_name: user.first_name.clone(),
            last_name: user.last_name.clone(),
            email: user.email.clone(),
            theme: user.theme.clone(),
            language: user.language.clone(),
            is_admin: user.is_admin,
        }
    }
}

/// `AuthResponseDto`.
///
/// `refreshToken` et `refreshCookieExpiresAt` sont toujours `null` dans le corps :
/// le contrôleur .NET les blanchit, le refresh token ne voyage que dans le cookie.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct AuthResponse {
    pub access_token: String,
    pub refresh_token: Option<String>,
    pub expires_in: i32,
    pub user: UserDto,
    pub refresh_cookie_expires_at: Option<DateTime<Utc>>,
}

/// Résultat interne d'un service d'authentification : la réponse HTTP plus les
/// éléments de cookie que seul le routeur sait poser.
#[derive(Debug, Clone)]
pub struct AuthOutcome {
    pub response: AuthResponse,
    /// Valeur brute du refresh token à poser en cookie.
    pub refresh_token: String,
    /// Expiration du cookie persistant ; `None` ⇒ cookie de session.
    pub cookie_expires_at: Option<DateTime<Utc>>,
}

#[cfg(test)]
mod tests {
    use super::*;

    fn errors_of<T: Validate>(value: &T) -> ValidationErrors {
        let mut errors = ValidationErrors::new();
        value.validate(&mut errors);
        errors
    }

    fn register(email: &str, password: &str) -> RegisterRequest {
        RegisterRequest {
            first_name: Some("Test".into()),
            last_name: Some("User".into()),
            email: Some(email.into()),
            password: Some(password.into()),
        }
    }

    #[test]
    fn valid_registration_passes_validation() {
        assert!(errors_of(&register("test@example.com", "Password123!")).is_empty());
    }

    #[test]
    fn invalid_email_is_rejected() {
        let errors = errors_of(&register("invalid-email", "Password123!"));
        assert_eq!(errors["Email"], vec!["Invalid email format"]);
    }

    #[test]
    fn weak_password_is_rejected() {
        let errors = errors_of(&register("test@example.com", "weak"));
        assert!(errors.contains_key("Password"));
    }

    #[test]
    fn missing_fields_are_reported_per_property() {
        let request = RegisterRequest {
            first_name: None,
            last_name: None,
            email: None,
            password: None,
        };
        let errors = errors_of(&request);
        for field in ["FirstName", "LastName", "Email", "Password"] {
            assert!(errors.contains_key(field), "{field} should be required");
        }
    }

    #[test]
    fn login_defaults_remember_me_to_false() {
        let request = LoginRequest {
            email: Some("a@b.com".into()),
            password: Some("x".into()),
            remember_me: None,
        };
        assert!(!request.remember_me());
        assert!(errors_of(&request).is_empty());
    }
}
