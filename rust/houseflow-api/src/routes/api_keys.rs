//! `ApiKeysController` : `POST` / `GET` `api/v1/users/api-keys`,
//! `DELETE api/v1/users/api-keys/{id:guid}`.

use axum::extract::State;
use axum::http::StatusCode;
use axum::response::{IntoResponse, Response};
use axum::routing::{delete, get};
use axum::{Json, Router};

use crate::audit::AuditContext;
use crate::auth::CurrentUser;
use crate::dto::api_keys::CreateApiKeyRequest;
use crate::error::AppResult;
use crate::extract::{GuidPath, ValidJson};
use crate::services::api_keys as service;
use crate::state::AppState;

pub fn routes() -> Router<AppState> {
    Router::new()
        .route("/api/v1/users/api-keys", get(list).post(create))
        // Route contrainte `{id:guid}` : un GUID invalide ne correspond pas ⇒ 404.
        .route("/api/v1/users/api-keys/{id}", delete(revoke))
}

async fn create(
    State(state): State<AppState>,
    user: CurrentUser,
    context: AuditContext,
    ValidJson(request): ValidJson<CreateApiKeyRequest>,
) -> AppResult<Response> {
    // Le contrôleur .NET utilise l'adresse de connexion, sans X-Forwarded-For.
    let ip = context.ip.clone();
    let created = service::create(&state.pool, &context, user.id, &request, ip.as_deref()).await?;
    Ok((StatusCode::CREATED, Json(created)).into_response())
}

async fn list(State(state): State<AppState>, user: CurrentUser) -> AppResult<Response> {
    let keys = service::list(&state.pool, user.id).await?;
    Ok(Json(keys).into_response())
}

async fn revoke(
    State(state): State<AppState>,
    user: CurrentUser,
    context: AuditContext,
    GuidPath(key_id): GuidPath,
) -> AppResult<Response> {
    service::revoke(&state.pool, &context, user.id, key_id).await?;
    Ok(StatusCode::NO_CONTENT.into_response())
}
