//! Backend HouseFlow en Rust — copie fonctionnelle du backend .NET
//! (`src/HouseFlow.API` + `Application` + `Infrastructure`).
//!
//! Le contrat, les décisions d'architecture et la procédure de test sont décrits dans
//! `rust/PORTING.md`. Règle de découpage : un handler valide, appelle un service et
//! met en forme la réponse ; la logique métier reste dans [`services`].
//!
//! Périmètre porté ici : socle du crate, infrastructure transverse (configuration,
//! base, erreurs, audit, authentification, en-têtes, CORS, santé, Swagger, tâches de
//! fond), le domaine **authentification / utilisateurs** (`AuthController`,
//! `UserSettingsController`, `ApiKeysController`) et la **collaboration**
//! (`MembersController`, `InvitationsController`, `AdminController`). Les maisons,
//! appareils et maintenances viennent ensuite.

pub mod audit;
pub mod auth;
pub mod clock;
pub mod config;
pub mod db;
pub mod dto;
pub mod error;
pub mod extract;
pub mod jobs;
pub mod middleware;
pub mod models;
pub mod routes;
pub mod services;
pub mod state;
pub mod validation;

/// Contrat OpenAPI du dépôt, embarqué dans le binaire.
pub const OPENAPI_YAML: &str = include_str!("../../../specs/openapi.yaml");
