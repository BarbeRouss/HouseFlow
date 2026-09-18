//! Authentification : JWT, clés API, extracteurs d'identité et portée des clés.

pub mod api_key;
pub mod cookie;
pub mod extractor;
pub mod jwt;
pub mod password;
pub mod scope;

pub use extractor::{AdminUser, CurrentUser, Identity, OptionalUser};
