//! Résolution de l'identité (schéma « MultiAuth » du `Program.cs`) et extracteurs.
//!
//! Sélection du schéma, identique à `ForwardDefaultSelector` :
//! 1. en-tête `X-API-Key` présent ⇒ clé API ;
//! 2. `Authorization: Bearer hf_…` ⇒ clé API ;
//! 3. sinon ⇒ JWT.
//!
//! Comme en ASP.NET, une authentification absente ou invalide ne rejette pas la
//! requête ici : elle laisse simplement l'identité vide, et ce sont les extracteurs
//! [`CurrentUser`] / [`AdminUser`] (l'équivalent de `[Authorize]`) qui répondent 401/403.

use axum::extract::{FromRequestParts, Request, State};
use axum::http::request::Parts;
use axum::middleware::Next;
use axum::response::Response;
use uuid::Uuid;

use crate::audit::AuditContext;
use crate::auth::{api_key, jwt};
use crate::error::AppError;
use crate::models::ApiKeyScope;
use crate::state::AppState;

/// Identité authentifiée de la requête courante.
///
/// `api_key_scope` est `None` pour une identité JWT : c'est ce qui distingue les deux
/// modes d'authentification dans tout le reste du code (le rôle admin ne voyage
/// jamais avec une clé API).
#[derive(Debug, Clone)]
pub struct CurrentUser {
    pub id: Uuid,
    pub email: String,
    pub is_admin: bool,
    pub api_key_scope: Option<ApiKeyScope>,
}

impl CurrentUser {
    /// Vrai si l'identité provient d'une clé API.
    pub fn is_api_key(&self) -> bool {
        self.api_key_scope.is_some()
    }
}

/// Identité résolue, posée dans les extensions de la requête par [`resolve_identity`].
#[derive(Debug, Clone, Default)]
pub struct Identity(pub Option<CurrentUser>);

/// Middleware d'authentification : résout l'identité et le contexte d'audit.
pub async fn resolve_identity(
    State(state): State<AppState>,
    mut request: Request,
    next: Next,
) -> Response {
    let user = authenticate(&state, request.headers()).await;

    let audit_context = AuditContext {
        user_id: user.as_ref().map(|u| u.id),
        username: user.as_ref().map(|u| u.email.clone()),
        ip: client_ip(request.headers(), &request),
        user_agent: header(request.headers(), "user-agent"),
    };

    request.extensions_mut().insert(Identity(user));
    request.extensions_mut().insert(audit_context);
    next.run(request).await
}

fn header(headers: &axum::http::HeaderMap, name: &str) -> Option<String> {
    headers
        .get(name)
        .and_then(|v| v.to_str().ok())
        .map(str::to_string)
}

/// IP cliente : premier élément de `X-Forwarded-For` (comme `UseForwardedHeaders`),
/// sinon l'adresse de la connexion.
fn client_ip(headers: &axum::http::HeaderMap, request: &Request) -> Option<String> {
    if let Some(forwarded) = header(headers, "x-forwarded-for") {
        if let Some(first) = forwarded.split(',').next() {
            let first = first.trim();
            if !first.is_empty() {
                return Some(first.to_string());
            }
        }
    }

    request
        .extensions()
        .get::<axum::extract::ConnectInfo<std::net::SocketAddr>>()
        .map(|info| info.0.ip().to_string())
}

async fn authenticate(state: &AppState, headers: &axum::http::HeaderMap) -> Option<CurrentUser> {
    let raw_api_key = header(headers, "x-api-key").or_else(|| {
        header(headers, "authorization")
            .filter(|value| value.starts_with("Bearer hf_"))
            .map(|value| value["Bearer ".len()..].to_string())
    });

    if let Some(raw_key) = raw_api_key {
        return authenticate_api_key(state, &raw_key).await;
    }

    let token = header(headers, "authorization")?
        .strip_prefix("Bearer ")?
        .to_string();
    let claims = jwt::verify_token(&state.config, &token)?;
    let id = claims.sub.parse::<Uuid>().ok()?;

    Some(CurrentUser {
        id,
        email: claims.email.clone(),
        is_admin: claims.is_admin(),
        api_key_scope: None,
    })
}

async fn authenticate_api_key(state: &AppState, raw_key: &str) -> Option<CurrentUser> {
    let identity = match api_key::validate_key(&state.pool, raw_key).await {
        Ok(identity) => identity?,
        Err(error) => {
            tracing::error!(error = %error, "API key validation failed");
            return None;
        }
    };

    // Le handler .NET va chercher l'e-mail de l'utilisateur pour ses claims ; une clé
    // orpheline échoue l'authentification.
    let email: Option<(String,)> = sqlx::query_as(r#"SELECT "Email" FROM "Users" WHERE "Id" = $1"#)
        .bind(identity.user_id)
        .fetch_optional(&state.pool)
        .await
        .ok()?;

    Some(CurrentUser {
        id: identity.user_id,
        email: email?.0,
        // Une identité de clé API ne porte jamais le rôle admin.
        is_admin: false,
        api_key_scope: Some(identity.scope),
    })
}

/// Équivalent de `[Authorize]` : 401 corps vide + `WWW-Authenticate: Bearer` sinon.
impl<S: Send + Sync> FromRequestParts<S> for CurrentUser {
    type Rejection = AppError;

    async fn from_request_parts(parts: &mut Parts, _state: &S) -> Result<Self, Self::Rejection> {
        parts
            .extensions
            .get::<Identity>()
            .and_then(|identity| identity.0.clone())
            .ok_or(AppError::Unauthenticated)
    }
}

/// Identité optionnelle, pour les points d'entrée anonymes qui adaptent leur réponse.
#[derive(Debug, Clone)]
pub struct OptionalUser(pub Option<CurrentUser>);

impl<S: Send + Sync> FromRequestParts<S> for OptionalUser {
    type Rejection = std::convert::Infallible;

    async fn from_request_parts(parts: &mut Parts, _state: &S) -> Result<Self, Self::Rejection> {
        Ok(OptionalUser(
            parts
                .extensions
                .get::<Identity>()
                .and_then(|identity| identity.0.clone()),
        ))
    }
}

/// Équivalent de `[Authorize(Roles = "Admin")]` : JWT d'administrateur uniquement.
///
/// Une clé API, même appartenant à un administrateur, ne porte pas le rôle : elle
/// obtient donc un 403 corps vide, comme `ForbidResult`.
#[derive(Debug, Clone)]
pub struct AdminUser(pub CurrentUser);

impl<S: Send + Sync> FromRequestParts<S> for AdminUser {
    type Rejection = AppError;

    async fn from_request_parts(parts: &mut Parts, state: &S) -> Result<Self, Self::Rejection> {
        let user = CurrentUser::from_request_parts(parts, state).await?;
        if user.is_admin && !user.is_api_key() {
            Ok(AdminUser(user))
        } else {
            Err(AppError::Forbidden)
        }
    }
}

/// Contexte d'audit de la requête, posé par [`resolve_identity`].
impl<S: Send + Sync> FromRequestParts<S> for AuditContext {
    type Rejection = std::convert::Infallible;

    async fn from_request_parts(parts: &mut Parts, _state: &S) -> Result<Self, Self::Rejection> {
        Ok(parts
            .extensions
            .get::<AuditContext>()
            .cloned()
            .unwrap_or_default())
    }
}
