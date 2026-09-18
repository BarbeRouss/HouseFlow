//! Logique métier — un module par service C#.
//!
//! Les handlers se contentent de valider, d'appeler un service et de formater la
//! réponse ; tout le reste vit ici, testable sans HTTP.
//!
pub mod admin;
pub mod api_keys;
pub mod auth;
pub mod bootstrap;
pub mod calculator;
pub mod devices;
pub mod houses;
pub mod maintenance;
pub mod members;
pub mod snapshots;
pub mod user_settings;
