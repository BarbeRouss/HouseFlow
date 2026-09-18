//! Extracteurs de requête reproduisant la liaison de modèle d'ASP.NET Core.
//!
//! * [`ValidJson`] : corps JSON + `DataAnnotations` (400 ProblemDetails, 415 si le
//!   `Content-Type` n'est pas JSON).
//! * [`GuidPath`] : segment de route contraint `{id:guid}` — un GUID invalide ne
//!   fait pas correspondre la route, donc **404**.
//! * [`LoosePath`] : paramètre `Guid` non contraint (`{houseId}`) — un GUID invalide
//!   est une erreur de liaison, donc **400 ProblemDetails**.

use axum::extract::rejection::JsonRejection;
use axum::extract::{FromRequest, FromRequestParts, Path, Request};
use axum::http::request::Parts;
use axum::Json;
use serde::de::DeserializeOwned;
use uuid::Uuid;

use crate::error::{AppError, ValidationErrors};
use crate::validation::Validate;

/// Corps JSON validé. Remplace `[FromBody] + DataAnnotations` du C#.
#[derive(Debug, Clone, Copy, Default)]
pub struct ValidJson<T>(pub T);

impl<S, T> FromRequest<S> for ValidJson<T>
where
    S: Send + Sync,
    T: DeserializeOwned + Validate,
{
    type Rejection = AppError;

    async fn from_request(req: Request, state: &S) -> Result<Self, Self::Rejection> {
        let value = match Json::<T>::from_request(req, state).await {
            Ok(Json(value)) => value,
            // Un corps non JSON est refusé avant toute désérialisation : 415, comme ASP.NET.
            Err(JsonRejection::MissingJsonContentType(_)) => {
                return Err(AppError::UnsupportedMediaType)
            }
            // JSON malformé, corps vide ou champ de type incompatible : 400 ProblemDetails.
            Err(rejection) => {
                return Err(AppError::invalid_value("$", rejection.body_text()));
            }
        };

        let mut errors = ValidationErrors::new();
        value.validate(&mut errors);
        if errors.is_empty() {
            Ok(ValidJson(value))
        } else {
            Err(AppError::Validation(errors))
        }
    }
}

/// Segment de route contraint (`{id:guid}`) : un GUID invalide ⇒ 404.
#[derive(Debug, Clone, Copy)]
pub struct GuidPath(pub Uuid);

impl<S: Send + Sync> FromRequestParts<S> for GuidPath {
    type Rejection = AppError;

    async fn from_request_parts(parts: &mut Parts, state: &S) -> Result<Self, Self::Rejection> {
        let Path(raw) = Path::<String>::from_request_parts(parts, state)
            .await
            .map_err(|_| AppError::RouteNotFound)?;
        raw.parse::<Uuid>()
            .map(GuidPath)
            .map_err(|_| AppError::RouteNotFound)
    }
}

/// Paramètre `Guid` non contraint (`{houseId}`) : un GUID invalide ⇒ 400 ProblemDetails.
///
/// Le nom du paramètre est nécessaire pour reproduire la clé du ProblemDetails
/// d'ASP.NET, d'où [`LoosePath::from_parts`] plutôt qu'un extracteur direct.
#[derive(Debug, Clone, Copy)]
pub struct LoosePath(pub Uuid);

impl LoosePath {
    /// Analyse un segment de route nommé, en produisant l'erreur de liaison d'ASP.NET.
    pub fn parse(param_name: &str, raw: &str) -> Result<Uuid, AppError> {
        raw.parse::<Uuid>().map_err(|_| {
            AppError::invalid_value(param_name, format!("The value '{raw}' is not valid."))
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn loose_path_reports_the_binding_error_of_aspnet() {
        let error = LoosePath::parse("houseId", "not-a-guid").unwrap_err();
        match error {
            AppError::Validation(errors) => {
                assert_eq!(
                    errors.get("houseId").unwrap(),
                    &vec!["The value 'not-a-guid' is not valid.".to_string()]
                );
            }
            other => panic!("expected a validation error, got {other:?}"),
        }
    }

    #[test]
    fn loose_path_accepts_a_valid_guid() {
        let id = Uuid::new_v4();
        assert_eq!(LoosePath::parse("houseId", &id.to_string()).unwrap(), id);
    }
}
