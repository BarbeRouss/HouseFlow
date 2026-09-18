//! Middlewares transverses.
//!
//! Le contexte d'audit (utilisateur, IP, user agent) est posé par le middleware
//! d'authentification [`crate::auth::extractor::resolve_identity`], puisqu'il a besoin
//! de l'identité résolue — c'est l'équivalent de `AuditContextMiddleware.cs`, qui
//! s'exécute lui aussi juste après `UseAuthentication`.

pub mod security_headers;
