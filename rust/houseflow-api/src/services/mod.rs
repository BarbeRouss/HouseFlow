//! Logique métier — un module par service C#.
//!
//! Les handlers se contentent de valider, d'appeler un service et de formater la
//! réponse ; tout le reste vit ici, testable sans HTTP.
//!
//! Phase 2/3 à ajouter : `houses`, `devices`, `maintenance`, `admin`, et le reste de
//! `members` (endpoints membres et invitations).

pub mod api_keys;
pub mod auth;
pub mod bootstrap;
pub mod calculator;
pub mod devices;
pub mod houses;
pub mod members;
pub mod snapshots;
pub mod user_settings;
