//! `MembersController` : `GET api/v1/collaborators`,
//! `GET api/v1/houses/{houseId}/members`, `PUT api/v1/members/{memberId}/role`,
//! `PUT api/v1/members/{memberId}/permissions`, `DELETE api/v1/members/{memberId}`.
//!
//! Tous les paramètres de route sont des `Guid` **non contraints** : une valeur
//! invalide est une erreur de liaison (400 ProblemDetails), pas un 404.

use axum::extract::{Path, State};
use axum::http::StatusCode;
use axum::response::{IntoResponse, Response};
use axum::routing::{delete, get, put};
use axum::{Json, Router};

use crate::audit::AuditContext;
use crate::auth::CurrentUser;
use crate::dto::members::{UpdateMemberPermissionsRequest, UpdateMemberRoleRequest};
use crate::error::{AppError, AppResult};
use crate::extract::{LoosePath, ValidJson};
use crate::models::HouseRole;
use crate::services::members as service;
use crate::state::AppState;

/// Message du contrôleur quand le rôle demandé n'existe pas.
const INVALID_ROLE: &str = "Invalid role. Must be CollaboratorRW, CollaboratorRO, or Tenant";

pub fn routes() -> Router<AppState> {
    Router::new()
        .route("/api/v1/collaborators", get(all_collaborators))
        .route("/api/v1/houses/{houseId}/members", get(house_members))
        .route("/api/v1/members/{memberId}/role", put(update_role))
        .route(
            "/api/v1/members/{memberId}/permissions",
            put(update_permissions),
        )
        .route("/api/v1/members/{memberId}", delete(remove))
}

/// `Enum.TryParse<HouseRole>(role, ignoreCase: true, …)` du contrôleur.
pub fn parse_role(raw: &str) -> AppResult<HouseRole> {
    raw.parse::<HouseRole>()
        .map_err(|_| AppError::BadRequest(INVALID_ROLE.to_string()))
}

async fn all_collaborators(
    State(state): State<AppState>,
    user: CurrentUser,
) -> AppResult<Response> {
    let result = service::get_all_collaborators(&state.pool, user.id).await?;
    Ok(Json(result).into_response())
}

async fn house_members(
    State(state): State<AppState>,
    user: CurrentUser,
    Path(house_id): Path<String>,
) -> AppResult<Response> {
    let house_id = LoosePath::parse("houseId", &house_id)?;
    let members = service::get_house_members(&state.pool, house_id, user.id).await?;
    Ok(Json(members).into_response())
}

async fn update_role(
    State(state): State<AppState>,
    user: CurrentUser,
    context: AuditContext,
    Path(member_id): Path<String>,
    ValidJson(request): ValidJson<UpdateMemberRoleRequest>,
) -> AppResult<Response> {
    let member_id = LoosePath::parse("memberId", &member_id)?;
    let role = parse_role(request.role.as_deref().unwrap_or_default())?;

    match service::update_member_role(&state.pool, &context, member_id, role, user.id).await? {
        Some(member) => Ok(Json(member).into_response()),
        // `NotFound()` : 404 sans corps.
        None => Err(AppError::RouteNotFound),
    }
}

async fn update_permissions(
    State(state): State<AppState>,
    user: CurrentUser,
    context: AuditContext,
    Path(member_id): Path<String>,
    ValidJson(request): ValidJson<UpdateMemberPermissionsRequest>,
) -> AppResult<Response> {
    let member_id = LoosePath::parse("memberId", &member_id)?;

    if service::update_member_permissions(&state.pool, &context, member_id, &request, user.id)
        .await?
    {
        Ok(StatusCode::NO_CONTENT.into_response())
    } else {
        Err(AppError::RouteNotFound)
    }
}

async fn remove(
    State(state): State<AppState>,
    user: CurrentUser,
    context: AuditContext,
    Path(member_id): Path<String>,
) -> AppResult<Response> {
    let member_id = LoosePath::parse("memberId", &member_id)?;

    if service::remove_member(&state.pool, &context, member_id, user.id).await? {
        Ok(StatusCode::NO_CONTENT.into_response())
    } else {
        Err(AppError::RouteNotFound)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn roles_are_parsed_without_regard_to_case() {
        assert_eq!(parse_role("tenant").unwrap(), HouseRole::Tenant);
        assert_eq!(
            parse_role("collaboratorrw").unwrap(),
            HouseRole::CollaboratorRW
        );
        // `Owner` est un rôle valide : c'est le service qui le refuse, en 400.
        assert_eq!(parse_role("Owner").unwrap(), HouseRole::Owner);
    }

    #[test]
    fn an_unknown_role_is_rejected_with_the_controller_message() {
        match parse_role("Chef").unwrap_err() {
            AppError::BadRequest(message) => assert_eq!(message, INVALID_ROLE),
            other => panic!("expected a bad request, got {other:?}"),
        }
    }
}
