//! Application de la portée des clés API — portage de `ApiKeyScopeEnforcementFilter`.
//!
//! GET/HEAD/OPTIONS passent toujours. Sur une écriture, une clé `ReadOnly` reçoit un
//! 403 avec le message exact du filtre C#. Les identités JWT ne sont pas concernées,
//! et `/api/v1/users/api-keys*` est exclu pour qu'un utilisateur puisse toujours
//! gérer ses propres clés.

use axum::extract::Request;
use axum::http::Method;
use axum::middleware::Next;
use axum::response::{IntoResponse, Response};

use crate::auth::extractor::Identity;
use crate::error::AppError;
use crate::models::ApiKeyScope;

/// Message renvoyé par le filtre .NET, repris au caractère près.
pub const READ_ONLY_MESSAGE: &str =
    "This API key has read-only access. A ReadWrite key is required for this operation.";

/// Préfixe exempté : gestion de ses propres clés.
const API_KEYS_PATH: &str = "/api/v1/users/api-keys";

fn is_read_only_method(method: &Method) -> bool {
    matches!(*method, Method::GET | Method::HEAD | Method::OPTIONS)
}

/// Vrai si la requête doit être refusée parce qu'elle écrit avec une clé en lecture seule.
pub fn is_denied(method: &Method, path: &str, scope: Option<ApiKeyScope>) -> bool {
    if is_read_only_method(method) {
        return false;
    }
    // Pas de portée ⇒ authentification JWT (ou anonyme) : le filtre ne s'applique pas.
    let Some(scope) = scope else {
        return false;
    };
    if path.len() >= API_KEYS_PATH.len()
        && path[..API_KEYS_PATH.len()].eq_ignore_ascii_case(API_KEYS_PATH)
    {
        return false;
    }
    scope == ApiKeyScope::ReadOnly
}

/// Middleware correspondant, branché juste après l'authentification.
pub async fn enforce_scope(request: Request, next: Next) -> Response {
    let scope = request
        .extensions()
        .get::<Identity>()
        .and_then(|identity| identity.0.as_ref())
        .and_then(|user| user.api_key_scope);

    if is_denied(request.method(), request.uri().path(), scope) {
        return AppError::ForbiddenWithMessage(READ_ONLY_MESSAGE.to_string()).into_response();
    }

    next.run(request).await
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn reads_are_always_allowed() {
        assert!(!is_denied(
            &Method::GET,
            "/api/v1/houses",
            Some(ApiKeyScope::ReadOnly)
        ));
        assert!(!is_denied(
            &Method::OPTIONS,
            "/api/v1/houses",
            Some(ApiKeyScope::ReadOnly)
        ));
    }

    #[test]
    fn writes_with_a_read_only_key_are_denied() {
        for method in [Method::POST, Method::PUT, Method::DELETE, Method::PATCH] {
            assert!(is_denied(
                &method,
                "/api/v1/houses",
                Some(ApiKeyScope::ReadOnly)
            ));
        }
    }

    #[test]
    fn writes_with_a_read_write_key_or_a_jwt_are_allowed() {
        assert!(!is_denied(
            &Method::POST,
            "/api/v1/houses",
            Some(ApiKeyScope::ReadWrite)
        ));
        assert!(!is_denied(&Method::POST, "/api/v1/houses", None));
    }

    #[test]
    fn managing_ones_own_api_keys_stays_allowed() {
        assert!(!is_denied(
            &Method::POST,
            "/api/v1/users/api-keys",
            Some(ApiKeyScope::ReadOnly)
        ));
        assert!(!is_denied(
            &Method::DELETE,
            "/API/V1/Users/Api-Keys/2f1c",
            Some(ApiKeyScope::ReadOnly)
        ));
    }
}
