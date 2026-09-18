//! Erreurs HTTP, reproduisant statut **et** corps du backend .NET.
//!
//! Correspondance (cf. `DomainExceptionFilter`, `AuthController`, ASP.NET) :
//!
//! | Cas | Statut | Corps |
//! |---|---|---|
//! | Authentification absente/invalide | 401 | vide + `WWW-Authenticate: Bearer` |
//! | `UnauthorizedAccessException` d'un service | 403 | vide (`ForbidResult`) |
//! | `/auth/login`, `/auth/refresh` | 401 | `{ "error": … }` |
//! | Clé API en lecture seule sur une écriture | 403 | `{ "error": … }` |
//! | `KeyNotFoundException` | 404 | `{ "error": … }` |
//! | `InvalidOperationException` | 400 | `{ "error": … }` |
//! | « already registered » au register | 409 | `{ "error": … }` |
//! | Validation des DTO | 400 | ProblemDetails ASP.NET |

use std::collections::BTreeMap;

use axum::http::{header, HeaderValue, StatusCode};
use axum::response::{IntoResponse, Response};
use axum::Json;
use serde_json::json;

/// Erreurs de validation : nom de propriété **PascalCase** → messages, comme le
/// `ModelState` d'ASP.NET.
pub type ValidationErrors = BTreeMap<String, Vec<String>>;

#[derive(Debug)]
pub enum AppError {
    /// 401, corps vide, `WWW-Authenticate: Bearer` — authentification absente ou invalide.
    Unauthenticated,
    /// 401 avec `{ "error": … }` — réservé à `/auth/login` et `/auth/refresh`.
    Unauthorized(String),
    /// 403, corps vide — `ForbidResult` d'ASP.NET (rôle manquant, accès refusé par un service).
    Forbidden,
    /// 403 avec `{ "error": … }` — portée de clé API insuffisante.
    ForbiddenWithMessage(String),
    /// 404 avec `{ "error": … }` — `KeyNotFoundException`.
    NotFound(String),
    /// 404 corps vide — route non appariée (contrainte `{id:guid}` d'ASP.NET).
    RouteNotFound,
    /// 400 avec `{ "error": … }` — `InvalidOperationException`.
    BadRequest(String),
    /// 409 avec `{ "error": … }` — e-mail déjà inscrit.
    Conflict(String),
    /// 400 ProblemDetails — échec de validation ou de liaison de modèle.
    Validation(ValidationErrors),
    /// 415 — corps non JSON.
    UnsupportedMediaType,
    /// 500 — toute erreur inattendue (base de données, sérialisation…).
    Internal(anyhow::Error),
}

impl AppError {
    /// ProblemDetails pour une valeur de route/corps invalide (`{houseId}` non GUID,
    /// JSON malformé…).
    pub fn invalid_value(field: &str, message: impl Into<String>) -> Self {
        let mut errors = ValidationErrors::new();
        errors.insert(field.to_string(), vec![message.into()]);
        AppError::Validation(errors)
    }
}

impl std::fmt::Display for AppError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            AppError::Unauthenticated => f.write_str("unauthenticated"),
            AppError::Unauthorized(m) => write!(f, "unauthorized: {m}"),
            AppError::Forbidden => f.write_str("forbidden"),
            AppError::ForbiddenWithMessage(m) => write!(f, "forbidden: {m}"),
            AppError::NotFound(m) => write!(f, "not found: {m}"),
            AppError::RouteNotFound => f.write_str("route not found"),
            AppError::BadRequest(m) => write!(f, "bad request: {m}"),
            AppError::Conflict(m) => write!(f, "conflict: {m}"),
            AppError::Validation(e) => write!(f, "validation failed: {e:?}"),
            AppError::UnsupportedMediaType => f.write_str("unsupported media type"),
            AppError::Internal(e) => write!(f, "internal error: {e}"),
        }
    }
}

impl std::error::Error for AppError {}

impl From<sqlx::Error> for AppError {
    fn from(error: sqlx::Error) -> Self {
        AppError::Internal(error.into())
    }
}

impl From<anyhow::Error> for AppError {
    fn from(error: anyhow::Error) -> Self {
        AppError::Internal(error)
    }
}

/// Corps ProblemDetails d'ASP.NET Core pour une 400 de validation.
pub fn problem_details(errors: &ValidationErrors) -> serde_json::Value {
    json!({
        "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
        "title": "One or more validation errors occurred.",
        "status": 400,
        "errors": errors,
    })
}

impl IntoResponse for AppError {
    fn into_response(self) -> Response {
        match self {
            AppError::Unauthenticated => {
                let mut response = StatusCode::UNAUTHORIZED.into_response();
                response
                    .headers_mut()
                    .insert(header::WWW_AUTHENTICATE, HeaderValue::from_static("Bearer"));
                response
            }
            AppError::Unauthorized(message) => {
                (StatusCode::UNAUTHORIZED, Json(json!({ "error": message }))).into_response()
            }
            AppError::Forbidden => StatusCode::FORBIDDEN.into_response(),
            AppError::ForbiddenWithMessage(message) => {
                (StatusCode::FORBIDDEN, Json(json!({ "error": message }))).into_response()
            }
            AppError::NotFound(message) => {
                (StatusCode::NOT_FOUND, Json(json!({ "error": message }))).into_response()
            }
            AppError::RouteNotFound => StatusCode::NOT_FOUND.into_response(),
            AppError::BadRequest(message) => {
                (StatusCode::BAD_REQUEST, Json(json!({ "error": message }))).into_response()
            }
            AppError::Conflict(message) => {
                (StatusCode::CONFLICT, Json(json!({ "error": message }))).into_response()
            }
            AppError::Validation(errors) => {
                let mut response =
                    (StatusCode::BAD_REQUEST, Json(problem_details(&errors))).into_response();
                response.headers_mut().insert(
                    header::CONTENT_TYPE,
                    HeaderValue::from_static("application/problem+json; charset=utf-8"),
                );
                response
            }
            AppError::UnsupportedMediaType => StatusCode::UNSUPPORTED_MEDIA_TYPE.into_response(),
            AppError::Internal(error) => {
                tracing::error!(error = %error, "unhandled error");
                StatusCode::INTERNAL_SERVER_ERROR.into_response()
            }
        }
    }
}

pub type AppResult<T> = Result<T, AppError>;

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn problem_details_uses_pascal_case_property_keys() {
        let mut errors = ValidationErrors::new();
        errors.insert("Email".into(), vec!["Invalid email format".into()]);

        let body = problem_details(&errors);
        assert_eq!(body["status"], 400);
        assert_eq!(body["title"], "One or more validation errors occurred.");
        assert_eq!(
            body["type"],
            "https://tools.ietf.org/html/rfc9110#section-15.5.1"
        );
        assert_eq!(body["errors"]["Email"][0], "Invalid email format");
    }

    #[test]
    fn unauthenticated_responses_are_empty_and_challenge_bearer() {
        let response = AppError::Unauthenticated.into_response();
        assert_eq!(response.status(), StatusCode::UNAUTHORIZED);
        assert_eq!(
            response.headers().get(header::WWW_AUTHENTICATE).unwrap(),
            "Bearer"
        );
    }

    #[test]
    fn forbidden_response_has_no_body() {
        let response = AppError::Forbidden.into_response();
        assert_eq!(response.status(), StatusCode::FORBIDDEN);
        assert!(response.headers().get(header::CONTENT_TYPE).is_none());
    }
}
