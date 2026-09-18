//! En-têtes de sécurité — portage exact de `SecurityHeadersMiddleware.cs`.

use axum::extract::Request;
use axum::http::{header::HeaderName, HeaderValue};
use axum::middleware::Next;
use axum::response::Response;

/// En-têtes posés sur **toutes** les réponses.
const HEADERS: &[(&str, &str)] = &[
    // Empêche le reniflage de type MIME.
    ("x-content-type-options", "nosniff"),
    // Empêche l'inclusion dans une iframe (clickjacking).
    ("x-frame-options", "DENY"),
    // Protection XSS des navigateurs historiques.
    ("x-xss-protection", "1; mode=block"),
    // Limite les informations de provenance.
    ("referrer-policy", "strict-origin-when-cross-origin"),
    // Restreint les fonctionnalités du navigateur.
    (
        "permissions-policy",
        "geolocation=(), microphone=(), camera=(), payment=()",
    ),
    // L'API ne sert que du JSON : politique de contenu minimale.
    (
        "content-security-policy",
        "default-src 'none'; frame-ancestors 'none'",
    ),
];

/// HSTS, uniquement sur HTTPS (comme `context.Request.IsHttps` côté .NET).
const HSTS: (&str, &str) = (
    "strict-transport-security",
    "max-age=31536000; includeSubDomains; preload",
);

pub async fn security_headers(request: Request, next: Next) -> Response {
    let is_https = request
        .headers()
        .get("x-forwarded-proto")
        .and_then(|value| value.to_str().ok())
        .map(|proto| proto.eq_ignore_ascii_case("https"))
        .unwrap_or_else(|| request.uri().scheme_str() == Some("https"));

    let mut response = next.run(request).await;
    let headers = response.headers_mut();

    for (name, value) in HEADERS {
        headers.append(
            HeaderName::from_static(name),
            HeaderValue::from_static(value),
        );
    }

    if is_https {
        headers.append(
            HeaderName::from_static(HSTS.0),
            HeaderValue::from_static(HSTS.1),
        );
    }

    response
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn every_header_name_and_value_is_a_valid_static_header() {
        for (name, value) in HEADERS.iter().chain(std::iter::once(&HSTS)) {
            HeaderName::from_static(name);
            HeaderValue::from_static(value);
        }
    }

    #[test]
    fn the_header_set_matches_the_csharp_middleware() {
        let names: Vec<&str> = HEADERS.iter().map(|(name, _)| *name).collect();
        assert_eq!(
            names,
            vec![
                "x-content-type-options",
                "x-frame-options",
                "x-xss-protection",
                "referrer-policy",
                "permissions-policy",
                "content-security-policy",
            ]
        );
    }
}
