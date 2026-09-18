//! Backend HouseFlow en Rust — copie fonctionnelle du backend .NET
//! (`src/HouseFlow.API` + `Application` + `Infrastructure`).
//!
//! Le contrat, les décisions d'architecture et la procédure de test sont décrits dans
//! `rust/PORTING.md`. Règle de découpage : un handler valide, appelle un service et
//! met en forme la réponse ; la logique métier reste dans [`services`].
//!
//! Périmètre porté ici (phase 1) : socle du crate, infrastructure transverse
//! (configuration, base, erreurs, audit, authentification, en-têtes, CORS, santé,
//! Swagger, tâches de fond) et le domaine **authentification / utilisateurs**
//! (`AuthController`, `UserSettingsController`, `ApiKeysController`). Les maisons,
//! membres, appareils, maintenances et l'administration viennent ensuite.

pub mod clock;
pub mod config;
pub mod db;
pub mod error;
pub mod models;
