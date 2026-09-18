//! `UserSettingsController` : `GET` / `PUT` `api/v1/users/settings`.

use axum::extract::State;
use axum::routing::get;
use axum::{Json, Router};

use crate::audit::AuditContext;
use crate::auth::CurrentUser;
use crate::dto::user_settings::{UpdateUserSettings, UserSettings};
use crate::error::AppResult;
use crate::extract::ValidJson;
use crate::services::user_settings as service;
use crate::state::AppState;

pub fn routes() -> Router<AppState> {
    Router::new().route(
        "/api/v1/users/settings",
        get(get_settings).put(update_settings),
    )
}

async fn get_settings(
    State(state): State<AppState>,
    user: CurrentUser,
) -> AppResult<Json<UserSettings>> {
    Ok(Json(service::get_settings(&state.pool, user.id).await?))
}

async fn update_settings(
    State(state): State<AppState>,
    user: CurrentUser,
    context: AuditContext,
    ValidJson(request): ValidJson<UpdateUserSettings>,
) -> AppResult<Json<UserSettings>> {
    Ok(Json(
        service::update_settings(&state.pool, &context, user.id, &request).await?,
    ))
}
