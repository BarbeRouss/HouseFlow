//! `AuthController` : inscription, connexion, rafraîchissement, révocation, déconnexion.

use axum::extract::{Query, State};
use axum::http::{header, HeaderMap, StatusCode};
use axum::response::{IntoResponse, Response};
use axum::routing::post;
use axum::{Json, Router};
use serde::Deserialize;
use serde_json::json;

use crate::audit::AuditContext;
use crate::auth::cookie;
use crate::auth::CurrentUser;
use crate::config::Config;
use crate::dto::auth::{AuthOutcome, LoginRequest, RegisterRequest};
use crate::error::{AppError, AppResult};
use crate::extract::ValidJson;
use crate::services::auth as service;
use crate::state::AppState;

pub fn routes() -> Router<AppState> {
    Router::new()
        .route("/api/v1/auth/register", post(register))
        .route("/api/v1/auth/login", post(login))
        .route("/api/v1/auth/refresh", post(refresh))
        .route("/api/v1/auth/revoke", post(revoke))
        .route("/api/v1/auth/logout", post(logout))
}

#[derive(Debug, Deserialize)]
struct RegisterQuery {
    #[serde(rename = "invitationToken")]
    invitation_token: Option<String>,
}

async fn register(
    State(state): State<AppState>,
    Query(query): Query<RegisterQuery>,
    context: AuditContext,
    headers: HeaderMap,
    ValidJson(request): ValidJson<RegisterRequest>,
) -> AppResult<Response> {
    let ip = context.ip.clone();
    let outcome = service::register(
        &state.pool,
        &state.config,
        &request,
        ip.as_deref(),
        query.invitation_token.as_deref(),
    )
    .await?;

    Ok(auth_response(&state.config, &headers, outcome))
}

async fn login(
    State(state): State<AppState>,
    context: AuditContext,
    headers: HeaderMap,
    ValidJson(request): ValidJson<LoginRequest>,
) -> AppResult<Response> {
    let ip = context.ip.clone();
    let outcome = service::login(&state.pool, &state.config, &request, ip.as_deref()).await?;

    Ok(auth_response(&state.config, &headers, outcome))
}

async fn refresh(
    State(state): State<AppState>,
    context: AuditContext,
    headers: HeaderMap,
) -> AppResult<Response> {
    let Some(token) = refresh_cookie(&headers) else {
        return Err(AppError::Unauthorized(
            "Refresh token not found".to_string(),
        ));
    };

    let ip = context.ip.clone();
    let outcome = service::refresh(&state.pool, &state.config, &token, ip.as_deref()).await?;

    Ok(auth_response(&state.config, &headers, outcome))
}

async fn revoke(
    State(state): State<AppState>,
    _user: CurrentUser,
    context: AuditContext,
    headers: HeaderMap,
) -> AppResult<Response> {
    let Some(token) = refresh_cookie(&headers) else {
        return Err(AppError::BadRequest("Refresh token not found".to_string()));
    };

    let ip = context.ip.clone();
    service::revoke_token(&state.pool, &context, &token, ip.as_deref()).await?;

    Ok(with_deleted_cookie(
        &state.config,
        &headers,
        Json(json!({ "message": "Token revoked successfully" })).into_response(),
    ))
}

/// La déconnexion réussit toujours : même si la révocation échoue, le cookie tombe.
async fn logout(
    State(state): State<AppState>,
    _user: CurrentUser,
    context: AuditContext,
    headers: HeaderMap,
) -> Response {
    if let Some(token) = refresh_cookie(&headers) {
        let ip = context.ip.clone();
        let _ = service::revoke_token(&state.pool, &context, &token, ip.as_deref()).await;
    }

    with_deleted_cookie(
        &state.config,
        &headers,
        Json(json!({ "message": "Logged out successfully" })).into_response(),
    )
}

// --- Cookie et adresse IP --------------------------------------------------

fn refresh_cookie(headers: &HeaderMap) -> Option<String> {
    cookie::read(headers.get(header::COOKIE).and_then(|v| v.to_str().ok()))
        .filter(|value| !value.is_empty())
}

/// Vrai si la requête d'origine est en HTTPS (directement ou via un proxy).
fn is_https(headers: &HeaderMap) -> bool {
    headers
        .get("x-forwarded-proto")
        .and_then(|value| value.to_str().ok())
        .map(|proto| proto.eq_ignore_ascii_case("https"))
        .unwrap_or(false)
}

fn auth_response(config: &Config, headers: &HeaderMap, outcome: AuthOutcome) -> Response {
    let set_cookie = cookie::build(
        &outcome.refresh_token,
        outcome.cookie_expires_at,
        config.cookie_same_site,
        is_https(headers),
    );

    let mut response = (StatusCode::OK, Json(outcome.response)).into_response();
    append_cookie(&mut response, &set_cookie);
    response
}

fn with_deleted_cookie(config: &Config, headers: &HeaderMap, mut response: Response) -> Response {
    let set_cookie = cookie::delete(config.cookie_same_site, is_https(headers));
    append_cookie(&mut response, &set_cookie);
    response
}

fn append_cookie(response: &mut Response, value: &str) {
    if let Ok(header_value) = header::HeaderValue::from_str(value) {
        response
            .headers_mut()
            .append(header::SET_COOKIE, header_value);
    }
}
