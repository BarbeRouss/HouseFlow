//! Assemblage du routeur.
//!
//! Ordre des couches, identique au pipeline `Program.cs`
//! (`UseMiddleware<SecurityHeaders>` → `UseCors` → `UseAuthentication` →
//! `UseAuthorization` + filtre de portée → contrôleurs) :
//!
//! ```text
//! security_headers → cors → auth (identité + contexte d'audit) → scope → handlers
//! ```
//!
//! En axum, la couche ajoutée en **dernier** est la plus externe : les `.layer()`
//! ci-dessous sont donc écrits dans l'ordre inverse du pipeline.
//!
//! Ajouter un domaine, c'est ajouter un `.merge(...)` ici et rien d'autre :
//! l'authentification, la portée des clés API et les en-têtes s'appliquent
//! automatiquement à toutes les routes.

pub mod admin;
pub mod api_keys;
pub mod auth;
pub mod devices;
pub mod houses;
pub mod invitations;
pub mod maintenance;
pub mod members;
pub mod user_settings;

use axum::extract::State;
use axum::http::{header, HeaderValue, Method, StatusCode};
use axum::response::{IntoResponse, Response};
use axum::routing::get;
use axum::Router;
use tower_http::cors::{AllowOrigin, CorsLayer};
use tower_http::trace::TraceLayer;

use crate::auth::extractor::resolve_identity;
use crate::auth::scope::enforce_scope;
use crate::config::Config;
use crate::middleware::security_headers::security_headers;
use crate::state::AppState;

/// Contrat OpenAPI, embarqué pour servir `/swagger/v1/swagger.json`.
const OPENAPI_YAML: &str = crate::OPENAPI_YAML;

/// Construit le routeur complet de l'application.
pub fn build(state: AppState) -> Router {
    let api = Router::new()
        .merge(auth::routes())
        .merge(user_settings::routes())
        .merge(api_keys::routes())
        .merge(houses::routes())
        .merge(devices::routes())
        .merge(maintenance::routes())
        .merge(members::routes())
        .merge(invitations::routes())
        .merge(admin::routes());

    let infrastructure = Router::new()
        .route("/health", get(health))
        .route("/alive", get(alive))
        .route("/swagger/index.html", get(swagger_ui))
        .route("/swagger/v1/swagger.json", get(swagger_document));

    api.merge(infrastructure)
        .layer(axum::middleware::from_fn(enforce_scope))
        .layer(axum::middleware::from_fn_with_state(
            state.clone(),
            resolve_identity,
        ))
        .layer(cors_layer(&state.config))
        .layer(axum::middleware::from_fn(security_headers))
        .layer(TraceLayer::new_for_http())
        .with_state(state)
}

/// CORS configurable par `CORS__ORIGINS` (`*` = toutes les origines).
fn cors_layer(config: &Config) -> CorsLayer {
    let origins = match &config.cors_origins {
        // `AllowCredentials` interdit le joker `*` : on renvoie l'origine demandée,
        // ce que fait aussi `SetIsOriginAllowed(_ => true)` côté ASP.NET.
        None => AllowOrigin::mirror_request(),
        Some(origins) => AllowOrigin::list(
            origins
                .iter()
                .filter_map(|origin| HeaderValue::from_str(origin).ok()),
        ),
    };

    CorsLayer::new()
        .allow_origin(origins)
        .allow_methods([Method::GET, Method::POST, Method::PUT, Method::DELETE])
        .allow_headers([header::AUTHORIZATION, header::CONTENT_TYPE])
        .allow_credentials(true)
}

/// `MapHealthChecks("/health")` : vérifie l'accès à la base.
async fn health(State(state): State<AppState>) -> Response {
    if crate::db::ping(&state.pool).await {
        plain_text(StatusCode::OK, "Healthy")
    } else {
        plain_text(StatusCode::SERVICE_UNAVAILABLE, "Unhealthy")
    }
}

/// `MapHealthChecks("/alive")` : aucune vérification, juste « le processus répond ».
async fn alive() -> Response {
    plain_text(StatusCode::OK, "Healthy")
}

fn plain_text(status: StatusCode, body: &'static str) -> Response {
    (
        status,
        [(header::CONTENT_TYPE, "text/plain; charset=utf-8")],
        body,
    )
        .into_response()
}

/// Page Swagger minimale : les scripts s'en servent comme sonde de démarrage.
async fn swagger_ui() -> Response {
    const PAGE: &str = r#"<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>HouseFlow API</title>
</head>
<body>
  <h1>HouseFlow API</h1>
  <p>OpenAPI document: <a href="/swagger/v1/swagger.json">/swagger/v1/swagger.json</a></p>
</body>
</html>"#;

    (
        StatusCode::OK,
        [(header::CONTENT_TYPE, "text/html; charset=utf-8")],
        PAGE,
    )
        .into_response()
}

/// Contrat OpenAPI converti en JSON au démarrage (`specs/openapi.yaml`).
async fn swagger_document() -> Response {
    match openapi_json() {
        Some(json) => (
            StatusCode::OK,
            [(header::CONTENT_TYPE, "application/json; charset=utf-8")],
            json,
        )
            .into_response(),
        // Repli : servir le YAML tel quel plutôt que rien.
        None => (
            StatusCode::OK,
            [(header::CONTENT_TYPE, "application/yaml; charset=utf-8")],
            OPENAPI_YAML,
        )
            .into_response(),
    }
}

/// Conversion YAML → JSON, mise en cache au premier appel.
fn openapi_json() -> Option<&'static str> {
    use std::sync::OnceLock;
    static JSON: OnceLock<Option<String>> = OnceLock::new();

    JSON.get_or_init(|| {
        serde_yaml::from_str::<serde_json::Value>(OPENAPI_YAML)
            .ok()
            .and_then(|value| serde_json::to_string(&value).ok())
    })
    .as_deref()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_embedded_openapi_document_converts_to_json() {
        let json = openapi_json().expect("specs/openapi.yaml should be valid YAML");
        let value: serde_json::Value = serde_json::from_str(json).unwrap();
        assert!(value.get("openapi").is_some());
        assert!(value.get("paths").is_some());
    }
}
