//! Portage des `DataAnnotations` des DTO générés (`Contracts.g.cs` +
//! `ContractValidation.cs`).
//!
//! Les messages reprennent ceux d'ASP.NET Core afin que le corps ProblemDetails soit
//! identique à celui du backend .NET, et les clés sont les noms de propriété
//! **PascalCase** (`Email`, `FirstName`, …).

use chrono::{DateTime, Utc};

use crate::error::ValidationErrors;

/// Un DTO de requête sait remplir la liste d'erreurs de validation.
pub trait Validate {
    fn validate(&self, errors: &mut ValidationErrors);
}

impl Validate for () {
    fn validate(&self, _errors: &mut ValidationErrors) {}
}

fn push(errors: &mut ValidationErrors, field: &str, message: String) {
    errors.entry(field.to_string()).or_default().push(message);
}

/// `[Required]` sur une chaîne : `null`, vide ou blanc échouent.
pub fn required(errors: &mut ValidationErrors, field: &str, value: Option<&String>) -> bool {
    match value {
        Some(v) if !v.trim().is_empty() => true,
        _ => {
            push(errors, field, format!("The {field} field is required."));
            false
        }
    }
}

/// `[StringLength(max, MinimumLength = min)]`.
pub fn string_length(
    errors: &mut ValidationErrors,
    field: &str,
    value: &str,
    min: usize,
    max: usize,
) {
    if value.chars().count() < min || value.chars().count() > max {
        push(
            errors,
            field,
            format!(
                "The field {field} must be a string with a minimum length of {min} and a maximum length of {max}."
            ),
        );
    }
}

/// `[StringLength(int.MaxValue, MinimumLength = min)]` (pas de borne haute utile).
pub fn min_length(errors: &mut ValidationErrors, field: &str, value: &str, min: usize) {
    if value.chars().count() < min {
        push(
            errors,
            field,
            format!("The field {field} must be a string with a minimum length of {min}."),
        );
    }
}

/// `[RegularExpression]` : le message d'ASP.NET cite le motif, on le reprend tel quel.
pub fn regular_expression(
    errors: &mut ValidationErrors,
    field: &str,
    pattern: &str,
    matches: bool,
    custom_message: Option<&str>,
) {
    if !matches {
        let message = custom_message.map(str::to_string).unwrap_or_else(|| {
            format!("The field {field} must match the regular expression '{pattern}'.")
        });
        push(errors, field, message);
    }
}

/// Motif `^(?=.*\d).{8,}$` du mot de passe : au moins 8 caractères dont un chiffre.
///
/// Écrit à la main parce que la crate `regex` ne gère pas les assertions arrière.
pub fn password_matches_policy(value: &str) -> bool {
    value.chars().count() >= 8 && value.chars().any(|c| c.is_ascii_digit())
}

/// Équivalent de `EmailAddressAttribute` : exactement une `@`, ni en tête ni en queue.
pub fn is_valid_email(value: &str) -> bool {
    let mut parts = value.split('@');
    let (Some(local), Some(domain), None) = (parts.next(), parts.next(), parts.next()) else {
        return false;
    };
    !local.is_empty() && !domain.is_empty() && !value.contains(char::is_whitespace)
}

/// Ajoute l'erreur `Invalid email format` de `ContractValidation.cs` si besoin.
pub fn email_format(errors: &mut ValidationErrors, field: &str, value: &str) {
    if !value.is_empty() && !is_valid_email(value) {
        push(errors, field, "Invalid email format".to_string());
    }
}

/// `[NotInFute]` : une date nulle est valide (c'est `[Required]` qui tranche),
/// une date passée ou présente est valide, une date future ne l'est pas.
///
/// Portage de `NotInFutureAttribute` (`HouseFlow.Application.Common`).
pub fn not_in_future(value: Option<DateTime<Utc>>) -> bool {
    match value {
        None => true,
        Some(date) => date <= Utc::now(),
    }
}

/// Variante « attribut » : pousse le message d'origine dans la liste d'erreurs.
pub fn not_in_future_field(
    errors: &mut ValidationErrors,
    field: &str,
    value: Option<DateTime<Utc>>,
) {
    if !not_in_future(value) {
        push(errors, field, "Date cannot be in the future".to_string());
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use chrono::Duration;

    // --- Portage de NotInFutureAttributeTests ---

    #[test]
    fn not_in_future_with_past_date_returns_true() {
        assert!(not_in_future(Some(Utc::now() - Duration::days(1))));
    }

    #[test]
    fn not_in_future_with_current_date_returns_true() {
        assert!(not_in_future(Some(Utc::now())));
    }

    #[test]
    fn not_in_future_with_future_date_returns_false() {
        assert!(!not_in_future(Some(Utc::now() + Duration::days(1))));
    }

    #[test]
    fn not_in_future_with_null_returns_true() {
        assert!(not_in_future(None));
    }

    #[test]
    fn not_in_future_with_far_future_date_returns_false() {
        assert!(!not_in_future(Some(Utc::now() + Duration::days(365))));
    }

    #[test]
    fn not_in_future_with_far_past_date_returns_true() {
        assert!(not_in_future(Some(Utc::now() - Duration::days(3650))));
    }

    #[test]
    fn not_in_future_field_uses_the_csharp_message() {
        let mut errors = ValidationErrors::new();
        not_in_future_field(&mut errors, "Date", Some(Utc::now() + Duration::days(1)));
        assert_eq!(errors["Date"], vec!["Date cannot be in the future"]);
    }

    // --- Règles de mot de passe et d'e-mail ---

    #[test]
    fn password_policy_requires_eight_characters_and_a_digit() {
        assert!(password_matches_policy("Password123!"));
        assert!(!password_matches_policy("weak"));
        assert!(!password_matches_policy("nodigitshere"));
        assert!(!password_matches_policy("Pass1"));
    }

    #[test]
    fn email_validation_matches_the_dotnet_attribute() {
        assert!(is_valid_email("test@example.com"));
        assert!(!is_valid_email("invalid-email"));
        assert!(!is_valid_email("@example.com"));
        assert!(!is_valid_email("test@"));
        assert!(!is_valid_email("a@b@c"));
    }

    #[test]
    fn required_reports_blank_values() {
        let mut errors = ValidationErrors::new();
        assert!(!required(&mut errors, "FirstName", Some(&"  ".to_string())));
        assert!(!required(&mut errors, "LastName", None));
        assert_eq!(
            errors["FirstName"],
            vec!["The FirstName field is required."]
        );
        assert!(errors.contains_key("LastName"));
    }
}
