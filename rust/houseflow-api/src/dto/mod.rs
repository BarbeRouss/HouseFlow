//! DTO de requête et de réponse, miroir de `HouseFlow.Application/DTOs` et de
//! `Generated/Contracts.g.cs`.
//!
//! Tous sont sérialisés en camelCase, valeurs `null` comprises (comportement par
//! défaut d'ASP.NET Core), et les dates en RFC 3339 UTC.

pub mod api_keys;
pub mod auth;
pub mod datetime;
pub mod devices;
pub mod houses;
pub mod maintenance;
pub mod user_settings;
