//! `InvitationsController` : `POST` / `GET api/v1/houses/{houseId}/invitations`,
//! `GET api/v1/invitations/{token}` (anonyme), `POST api/v1/invitations/{token}/accept`,
//! `DELETE api/v1/invitations/{invitationId}`.
//!
//! `GET {token}` et `DELETE {invitationId}` partagent le même gabarit de route : le
//! segment est lu brut, puis interprété comme jeton ou comme `Guid` selon le verbe.

use axum::extract::{Path, State};
use axum::http::{header, StatusCode};
use axum::response::{IntoResponse, Response};
use axum::routing::{get, post};
use axum::{Json, Router};

use crate::audit::AuditContext;
use crate::auth::CurrentUser;
use crate::dto::members::CreateInvitationRequest;
use crate::error::{AppError, AppResult};
use crate::extract::{LoosePath, ValidJson};
use crate::routes::members::parse_role;
use crate::services::members as service;
use crate::state::AppState;

pub fn routes() -> Router<AppState> {
    Router::new()
        .route(
            "/api/v1/houses/{houseId}/invitations",
            get(house_invitations).post(create),
        )
        .route("/api/v1/invitations/{token}", get(info).delete(revoke))
        .route("/api/v1/invitations/{token}/accept", post(accept))
}

async fn create(
    State(state): State<AppState>,
    user: CurrentUser,
    context: AuditContext,
    Path(house_id): Path<String>,
    ValidJson(request): ValidJson<CreateInvitationRequest>,
) -> AppResult<Response> {
    let house_id = LoosePath::parse("houseId", &house_id)?;
    let role = parse_role(request.role.as_deref().unwrap_or_default())?;

    let invitation =
        service::create_invitation(&state.pool, &context, house_id, role, user.id).await?;

    // `CreatedAtAction(nameof(GetInvitationInfo), new { token })`.
    let location = format!("/api/v1/invitations/{}", invitation.token);
    Ok((
        StatusCode::CREATED,
        [(header::LOCATION, location)],
        Json(invitation),
    )
        .into_response())
}

async fn house_invitations(
    State(state): State<AppState>,
    user: CurrentUser,
    Path(house_id): Path<String>,
) -> AppResult<Response> {
    let house_id = LoosePath::parse("houseId", &house_id)?;
    let invitations = service::get_house_invitations(&state.pool, house_id, user.id).await?;
    Ok(Json(invitations).into_response())
}

/// Point d'entrée **anonyme** : aucune authentification n'est exigée.
async fn info(State(state): State<AppState>, Path(token): Path<String>) -> AppResult<Response> {
    match service::get_invitation_info(&state.pool, &token).await? {
        Some(info) => Ok(Json(info).into_response()),
        // `NotFound()` : 404 sans corps.
        None => Err(AppError::RouteNotFound),
    }
}

async fn accept(
    State(state): State<AppState>,
    user: CurrentUser,
    context: AuditContext,
    Path(token): Path<String>,
) -> AppResult<Response> {
    let result = service::accept_invitation(&state.pool, &context, &token, user.id).await?;
    Ok(Json(result).into_response())
}

async fn revoke(
    State(state): State<AppState>,
    user: CurrentUser,
    context: AuditContext,
    Path(invitation_id): Path<String>,
) -> AppResult<Response> {
    let invitation_id = LoosePath::parse("invitationId", &invitation_id)?;

    if service::revoke_invitation(&state.pool, &context, invitation_id, user.id).await? {
        Ok(StatusCode::NO_CONTENT.into_response())
    } else {
        Err(AppError::RouteNotFound)
    }
}
