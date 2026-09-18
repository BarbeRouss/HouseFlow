//! `MaintenanceController`, `UpcomingTasksController` et `MaintenanceInstancesController` :
//! `PUT` / `DELETE` `api/v1/maintenance-types/{typeId}`,
//! `POST api/v1/maintenance-types/{typeId}/instances`,
//! `GET api/v1/upcoming-tasks`,
//! `PUT` / `DELETE` `api/v1/maintenance-instances/{instanceId}`.

use axum::extract::{Path, Query, State};
use axum::http::{header, StatusCode};
use axum::response::{IntoResponse, Response};
use axum::routing::{get, post, put};
use axum::{Json, Router};
use serde::Deserialize;

use crate::audit::AuditContext;
use crate::auth::CurrentUser;
use crate::dto::maintenance::{
    LogMaintenanceRequest, UpdateMaintenanceInstanceRequest, UpdateMaintenanceTypeRequest,
};
use crate::error::{AppError, AppResult};
use crate::extract::{LoosePath, ValidJson};
use crate::services::maintenance as service;
use crate::state::AppState;

pub fn routes() -> Router<AppState> {
    Router::new()
        .route(
            "/api/v1/maintenance-types/{typeId}",
            put(update_type).delete(delete_type),
        )
        .route(
            "/api/v1/maintenance-types/{typeId}/instances",
            post(log_maintenance),
        )
        .route("/api/v1/upcoming-tasks", get(upcoming_tasks))
        .route(
            "/api/v1/maintenance-instances/{instanceId}",
            put(update_instance).delete(delete_instance),
        )
}

async fn update_type(
    State(state): State<AppState>,
    user: CurrentUser,
    context: AuditContext,
    Path(type_id): Path<String>,
    ValidJson(request): ValidJson<UpdateMaintenanceTypeRequest>,
) -> AppResult<Response> {
    let type_id = LoosePath::parse("typeId", &type_id)?;
    match service::update_maintenance_type(&state.pool, &context, type_id, user.id, &request)
        .await?
    {
        Some(updated) => Ok(Json(updated).into_response()),
        // `NotFound()` d'ASP.NET : 404 sans corps.
        None => Err(AppError::RouteNotFound),
    }
}

async fn delete_type(
    State(state): State<AppState>,
    user: CurrentUser,
    context: AuditContext,
    Path(type_id): Path<String>,
) -> AppResult<Response> {
    let type_id = LoosePath::parse("typeId", &type_id)?;
    if service::delete_maintenance_type(&state.pool, &context, type_id, user.id).await? {
        Ok(StatusCode::NO_CONTENT.into_response())
    } else {
        Err(AppError::RouteNotFound)
    }
}

async fn log_maintenance(
    State(state): State<AppState>,
    user: CurrentUser,
    context: AuditContext,
    Path(type_id): Path<String>,
    ValidJson(request): ValidJson<LogMaintenanceRequest>,
) -> AppResult<Response> {
    let type_id = LoosePath::parse("typeId", &type_id)?;
    let instance =
        service::log_maintenance(&state.pool, &context, type_id, user.id, &request).await?;
    // `CreatedAtAction(nameof(LogMaintenance), new { typeId })`.
    let location = format!("/api/v1/maintenance-types/{type_id}/instances");
    Ok((
        StatusCode::CREATED,
        [(header::LOCATION, location)],
        Json(instance),
    )
        .into_response())
}

/// `[FromQuery] int? limit` : absent ou vide ⇒ aucune limite, non numérique ⇒ 400.
#[derive(Debug, Deserialize)]
struct UpcomingTasksQuery {
    limit: Option<String>,
}

impl UpcomingTasksQuery {
    fn limit(&self) -> AppResult<Option<i32>> {
        match self.limit.as_deref() {
            None | Some("") => Ok(None),
            Some(raw) => raw.parse::<i32>().map(Some).map_err(|_| {
                AppError::invalid_value("limit", format!("The value '{raw}' is not valid."))
            }),
        }
    }
}

async fn upcoming_tasks(
    State(state): State<AppState>,
    user: CurrentUser,
    Query(query): Query<UpcomingTasksQuery>,
) -> AppResult<Response> {
    let tasks = service::get_upcoming_tasks(&state.pool, user.id, query.limit()?).await?;
    Ok(Json(tasks).into_response())
}

async fn update_instance(
    State(state): State<AppState>,
    user: CurrentUser,
    context: AuditContext,
    Path(instance_id): Path<String>,
    ValidJson(request): ValidJson<UpdateMaintenanceInstanceRequest>,
) -> AppResult<Response> {
    let instance_id = LoosePath::parse("instanceId", &instance_id)?;
    match service::update_maintenance_instance(
        &state.pool,
        &context,
        instance_id,
        user.id,
        &request,
    )
    .await?
    {
        Some(instance) => Ok(Json(instance).into_response()),
        None => Err(AppError::RouteNotFound),
    }
}

async fn delete_instance(
    State(state): State<AppState>,
    user: CurrentUser,
    context: AuditContext,
    Path(instance_id): Path<String>,
) -> AppResult<Response> {
    let instance_id = LoosePath::parse("instanceId", &instance_id)?;
    if service::delete_maintenance_instance(&state.pool, &context, instance_id, user.id).await? {
        Ok(StatusCode::NO_CONTENT.into_response())
    } else {
        Err(AppError::RouteNotFound)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn query(limit: Option<&str>) -> UpcomingTasksQuery {
        UpcomingTasksQuery {
            limit: limit.map(str::to_string),
        }
    }

    #[test]
    fn an_absent_or_empty_limit_means_no_limit() {
        assert_eq!(query(None).limit().unwrap(), None);
        assert_eq!(query(Some("")).limit().unwrap(), None);
    }

    #[test]
    fn a_numeric_limit_is_read_as_is() {
        assert_eq!(query(Some("2")).limit().unwrap(), Some(2));
        assert_eq!(query(Some("-1")).limit().unwrap(), Some(-1));
    }

    #[test]
    fn a_non_numeric_limit_is_a_binding_error() {
        let error = query(Some("deux")).limit().unwrap_err();
        match error {
            AppError::Validation(errors) => {
                assert_eq!(
                    errors["limit"],
                    vec!["The value 'deux' is not valid.".to_string()]
                );
            }
            other => panic!("expected a validation error, got {other:?}"),
        }
    }
}
