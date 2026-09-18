//! `DevicesController` : `GET` / `POST` `api/v1/houses/{houseId}/devices`,
//! `GET` / `PUT` / `DELETE` `api/v1/devices/{deviceId}`,
//! `GET` / `POST` `api/v1/devices/{deviceId}/maintenance-types`,
//! `GET api/v1/devices/{deviceId}/maintenance-history`.
//!
//! Aucun segment n'est contraint côté C# : un GUID invalide donne 400, pas 404.

use axum::extract::{Path, State};
use axum::http::{header, StatusCode};
use axum::response::{IntoResponse, Response};
use axum::routing::get;
use axum::{Json, Router};

use crate::audit::AuditContext;
use crate::auth::CurrentUser;
use crate::dto::devices::{CreateDeviceRequest, UpdateDeviceRequest};
use crate::dto::maintenance::CreateMaintenanceTypeRequest;
use crate::error::{AppError, AppResult};
use crate::extract::{LoosePath, ValidJson};
use crate::services::devices as service;
use crate::services::maintenance;
use crate::state::AppState;

pub fn routes() -> Router<AppState> {
    Router::new()
        .route(
            "/api/v1/houses/{houseId}/devices",
            get(list_house_devices).post(create),
        )
        .route(
            "/api/v1/devices/{deviceId}",
            get(detail).put(update).delete(remove),
        )
        .route(
            "/api/v1/devices/{deviceId}/maintenance-types",
            get(list_maintenance_types).post(create_maintenance_type),
        )
        .route(
            "/api/v1/devices/{deviceId}/maintenance-history",
            get(maintenance_history),
        )
}

async fn list_house_devices(
    State(state): State<AppState>,
    user: CurrentUser,
    Path(house_id): Path<String>,
) -> AppResult<Response> {
    let house_id = LoosePath::parse("houseId", &house_id)?;
    let devices = service::get_house_devices(&state.pool, house_id, user.id).await?;
    Ok(Json(devices).into_response())
}

async fn create(
    State(state): State<AppState>,
    user: CurrentUser,
    context: AuditContext,
    Path(house_id): Path<String>,
    ValidJson(request): ValidJson<CreateDeviceRequest>,
) -> AppResult<Response> {
    let house_id = LoosePath::parse("houseId", &house_id)?;
    let device = service::create_device(&state.pool, &context, house_id, user.id, &request).await?;
    // `CreatedAtAction(nameof(GetDevice), new { deviceId = device.Id })`.
    let location = format!("/api/v1/devices/{}", device.id);
    Ok((
        StatusCode::CREATED,
        [(header::LOCATION, location)],
        Json(device),
    )
        .into_response())
}

async fn detail(
    State(state): State<AppState>,
    user: CurrentUser,
    Path(device_id): Path<String>,
) -> AppResult<Response> {
    let device_id = LoosePath::parse("deviceId", &device_id)?;
    match service::get_device_detail(&state.pool, device_id, user.id).await? {
        Some(device) => Ok(Json(device).into_response()),
        None => Err(AppError::RouteNotFound),
    }
}

async fn update(
    State(state): State<AppState>,
    user: CurrentUser,
    context: AuditContext,
    Path(device_id): Path<String>,
    ValidJson(request): ValidJson<UpdateDeviceRequest>,
) -> AppResult<Response> {
    let device_id = LoosePath::parse("deviceId", &device_id)?;
    match service::update_device(&state.pool, &context, device_id, user.id, &request).await? {
        Some(device) => Ok(Json(device).into_response()),
        None => Err(AppError::RouteNotFound),
    }
}

async fn remove(
    State(state): State<AppState>,
    user: CurrentUser,
    context: AuditContext,
    Path(device_id): Path<String>,
) -> AppResult<Response> {
    let device_id = LoosePath::parse("deviceId", &device_id)?;
    if service::delete_device(&state.pool, &context, device_id, user.id).await? {
        Ok(StatusCode::NO_CONTENT.into_response())
    } else {
        Err(AppError::RouteNotFound)
    }
}

async fn list_maintenance_types(
    State(state): State<AppState>,
    user: CurrentUser,
    Path(device_id): Path<String>,
) -> AppResult<Response> {
    let device_id = LoosePath::parse("deviceId", &device_id)?;
    let types = maintenance::get_device_maintenance_types(&state.pool, device_id, user.id).await?;
    Ok(Json(types).into_response())
}

async fn create_maintenance_type(
    State(state): State<AppState>,
    user: CurrentUser,
    context: AuditContext,
    Path(device_id): Path<String>,
    ValidJson(request): ValidJson<CreateMaintenanceTypeRequest>,
) -> AppResult<Response> {
    let device_id = LoosePath::parse("deviceId", &device_id)?;
    let created =
        maintenance::create_maintenance_type(&state.pool, &context, device_id, user.id, &request)
            .await?;
    // `CreatedAtAction(nameof(GetDeviceMaintenanceTypes), new { deviceId })`.
    let location = format!("/api/v1/devices/{device_id}/maintenance-types");
    Ok((
        StatusCode::CREATED,
        [(header::LOCATION, location)],
        Json(created),
    )
        .into_response())
}

async fn maintenance_history(
    State(state): State<AppState>,
    user: CurrentUser,
    Path(device_id): Path<String>,
) -> AppResult<Response> {
    let device_id = LoosePath::parse("deviceId", &device_id)?;
    let history =
        maintenance::get_device_maintenance_history(&state.pool, device_id, user.id).await?;
    Ok(Json(history).into_response())
}
