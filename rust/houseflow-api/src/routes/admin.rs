//! `AdminController` : `GET api/v1/admin/stats`, `GET api/v1/admin/users`,
//! `PUT api/v1/admin/users/{id:guid}/admin`.
//!
//! Le contrôleur porte `[Authorize(Roles = "Admin")]` : l'extracteur [`AdminUser`]
//! renvoie 401 sans authentification et 403 corps vide sinon — une clé API, même
//! celle d'un administrateur, ne porte jamais le rôle.
//!
//! `{id:guid}` est une route **contrainte** : un GUID invalide ne correspond à aucune
//! route et répond 404.

use axum::extract::{Path, Query, State};
use axum::response::{IntoResponse, Response};
use axum::routing::{get, put};
use axum::{Json, Router};
use serde::Deserialize;

use crate::audit::AuditContext;
use crate::auth::AdminUser;
use crate::dto::admin::SetUserAdminRequest;
use crate::error::{AppError, AppResult};
use crate::extract::ValidJson;
use crate::services::admin as service;
use crate::state::AppState;
use uuid::Uuid;

pub fn routes() -> Router<AppState> {
    Router::new()
        .route("/api/v1/admin/stats", get(stats))
        .route("/api/v1/admin/users", get(users))
        .route("/api/v1/admin/users/{id}/admin", put(set_admin))
}

/// `[FromQuery] string? search, int page = 1, int pageSize = 20`.
#[derive(Debug, Clone, Deserialize)]
struct UsersQuery {
    search: Option<String>,
    page: Option<String>,
    #[serde(rename = "pageSize")]
    page_size: Option<String>,
}

impl UsersQuery {
    /// Liaison d'un entier de requête : une valeur illisible est une erreur de
    /// liaison ASP.NET (400 ProblemDetails), pas une valeur par défaut.
    fn integer(field: &str, raw: Option<&String>, default: i64) -> AppResult<i64> {
        match raw {
            None => Ok(default),
            Some(value) => value.parse::<i64>().map_err(|_| {
                AppError::invalid_value(field, format!("The value '{value}' is not valid."))
            }),
        }
    }
}

async fn stats(State(state): State<AppState>, _admin: AdminUser) -> AppResult<Response> {
    Ok(Json(service::get_stats(&state.pool).await?).into_response())
}

async fn users(
    State(state): State<AppState>,
    _admin: AdminUser,
    Query(query): Query<UsersQuery>,
) -> AppResult<Response> {
    let page = UsersQuery::integer("page", query.page.as_ref(), 1)?;
    let page_size = UsersQuery::integer("pageSize", query.page_size.as_ref(), 20)?;

    let result = service::get_users(&state.pool, query.search.as_deref(), page, page_size).await?;
    Ok(Json(result).into_response())
}

async fn set_admin(
    State(state): State<AppState>,
    admin: AdminUser,
    context: AuditContext,
    Path(id): Path<String>,
    ValidJson(request): ValidJson<SetUserAdminRequest>,
) -> AppResult<Response> {
    // Contrainte `{id:guid}` : pas de correspondance de route ⇒ 404 corps vide.
    let target_id = id.parse::<Uuid>().map_err(|_| AppError::RouteNotFound)?;

    let user = service::set_admin(
        &state.pool,
        &context,
        admin.0.id,
        target_id,
        request.is_admin(),
    )
    .await?;

    Ok(Json(user).into_response())
}
