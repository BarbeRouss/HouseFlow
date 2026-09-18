//! `HousesController` : `GET` / `POST` `api/v1/houses`,
//! `GET` / `PUT` / `DELETE` `api/v1/houses/{houseId}`.
//!
//! `{houseId}` n'est pas contraint côté C# : un GUID invalide est une erreur de
//! liaison (400 ProblemDetails), pas une route non trouvée — d'où [`LoosePath`].

use axum::extract::{Path, State};
use axum::http::{header, StatusCode};
use axum::response::{IntoResponse, Response};
use axum::routing::get;
use axum::{Json, Router};

use crate::audit::AuditContext;
use crate::auth::CurrentUser;
use crate::dto::houses::{CreateHouseRequest, UpdateHouseRequest};
use crate::error::{AppError, AppResult};
use crate::extract::{LoosePath, ValidJson};
use crate::services::houses as service;
use crate::state::AppState;

pub fn routes() -> Router<AppState> {
    Router::new()
        .route("/api/v1/houses", get(list).post(create))
        .route(
            "/api/v1/houses/{houseId}",
            get(detail).put(update).delete(remove),
        )
}

async fn list(State(state): State<AppState>, user: CurrentUser) -> AppResult<Response> {
    Ok(Json(service::get_user_houses(&state.pool, user.id).await?).into_response())
}

async fn create(
    State(state): State<AppState>,
    user: CurrentUser,
    context: AuditContext,
    ValidJson(request): ValidJson<CreateHouseRequest>,
) -> AppResult<Response> {
    let house = service::create_house(&state.pool, &context, user.id, &request).await?;
    // `CreatedAtAction(nameof(GetHouse), new { houseId = house.Id })`.
    let location = format!("/api/v1/houses/{}", house.id);
    Ok((
        StatusCode::CREATED,
        [(header::LOCATION, location)],
        Json(house),
    )
        .into_response())
}

async fn detail(
    State(state): State<AppState>,
    user: CurrentUser,
    Path(house_id): Path<String>,
) -> AppResult<Response> {
    let house_id = LoosePath::parse("houseId", &house_id)?;
    match service::get_house_detail(&state.pool, house_id, user.id).await? {
        Some(house) => Ok(Json(house).into_response()),
        // `NotFound()` d'ASP.NET : 404 sans corps.
        None => Err(AppError::RouteNotFound),
    }
}

async fn update(
    State(state): State<AppState>,
    user: CurrentUser,
    context: AuditContext,
    Path(house_id): Path<String>,
    ValidJson(request): ValidJson<UpdateHouseRequest>,
) -> AppResult<Response> {
    let house_id = LoosePath::parse("houseId", &house_id)?;
    match service::update_house(&state.pool, &context, house_id, user.id, &request).await? {
        Some(house) => Ok(Json(house).into_response()),
        None => Err(AppError::RouteNotFound),
    }
}

async fn remove(
    State(state): State<AppState>,
    user: CurrentUser,
    context: AuditContext,
    Path(house_id): Path<String>,
) -> AppResult<Response> {
    let house_id = LoosePath::parse("houseId", &house_id)?;
    if service::delete_house(&state.pool, &context, house_id, user.id).await? {
        Ok(StatusCode::NO_CONTENT.into_response())
    } else {
        Err(AppError::RouteNotFound)
    }
}
