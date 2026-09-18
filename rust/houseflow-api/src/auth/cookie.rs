//! Cookie de refresh token.
//!
//! L'en-tête `Set-Cookie` est écrit à la main pour coller au rendu d'ASP.NET Core :
//! attributs **en minuscules** (`expires`, `path`, `secure`, `samesite`, `httponly`)
//! et date au format RFC 1123. Les tests d'intégration lisent ces attributs
//! littéralement (`setCookie.Should().Contain("httponly")`).
//!
//! La suppression réutilise exactement les mêmes attributs : un navigateur n'honore
//! la suppression que si `path`, `samesite` et `secure` correspondent.

use chrono::{DateTime, TimeZone, Utc};

use crate::config::SameSite;

pub const COOKIE_NAME: &str = "refreshToken";

/// Rendu RFC 1123 attendu dans l'attribut `expires` (`Thu, 18 Sep 2026 21:30:00 GMT`).
fn format_expires(value: DateTime<Utc>) -> String {
    value.format("%a, %d %b %Y %H:%M:%S GMT").to_string()
}

/// Construit la valeur de l'en-tête `Set-Cookie`.
///
/// * `expires = None` ⇒ cookie de session (aucun `expires`, aucun `max-age`) ;
/// * `secure` est posé si la requête est en HTTPS ou si `SameSite=None`.
pub fn build(
    value: &str,
    expires: Option<DateTime<Utc>>,
    same_site: SameSite,
    is_https: bool,
) -> String {
    let mut cookie = format!("{COOKIE_NAME}={value}");

    if let Some(expires) = expires {
        cookie.push_str("; expires=");
        cookie.push_str(&format_expires(expires));
    }

    cookie.push_str("; path=/");

    if is_https || same_site == SameSite::None {
        cookie.push_str("; secure");
    }

    cookie.push_str("; samesite=");
    cookie.push_str(same_site.as_attribute());
    cookie.push_str("; httponly");

    cookie
}

/// Cookie de suppression : valeur vide et expiration à l'époque Unix.
pub fn delete(same_site: SameSite, is_https: bool) -> String {
    let epoch = Utc.timestamp_opt(0, 0).single().expect("epoch is valid");
    build("", Some(epoch), same_site, is_https)
}

/// Lit la valeur de `refreshToken` dans l'en-tête `Cookie`.
///
/// La valeur est du base64 : elle contient des `=` de remplissage, d'où la découpe
/// sur le **premier** `=` seulement.
pub fn read(cookie_header: Option<&str>) -> Option<String> {
    let header = cookie_header?;
    header.split(';').find_map(|part| {
        let part = part.trim();
        let (name, value) = part.split_once('=')?;
        (name.trim() == COOKIE_NAME).then(|| value.to_string())
    })
}

#[cfg(test)]
mod tests {
    use super::*;
    use chrono::Duration;

    #[test]
    fn a_session_cookie_has_neither_expires_nor_max_age() {
        let cookie = build("abc", None, SameSite::Lax, false);
        let lower = cookie.to_lowercase();
        assert!(!lower.contains("expires="));
        assert!(!lower.contains("max-age="));
        assert!(cookie.contains("httponly"));
        assert_eq!(cookie, "refreshToken=abc; path=/; samesite=lax; httponly");
    }

    #[test]
    fn a_persistent_cookie_carries_an_rfc1123_expiry() {
        let expires = Utc
            .with_ymd_and_hms(2026, 9, 18, 21, 30, 0)
            .single()
            .unwrap();
        let cookie = build("abc", Some(expires), SameSite::Lax, false);
        assert!(cookie.contains("expires=Fri, 18 Sep 2026 21:30:00 GMT"));
        assert!(cookie.to_lowercase().contains("samesite=lax"));
    }

    #[test]
    fn same_site_none_forces_the_secure_attribute() {
        let cookie = build("abc", None, SameSite::None, false);
        assert!(cookie.contains("; secure"));
        assert!(cookie.contains("samesite=none"));
    }

    #[test]
    fn https_requests_get_a_secure_cookie() {
        assert!(build("abc", None, SameSite::Lax, true).contains("; secure"));
    }

    #[test]
    fn deletion_reuses_the_same_attributes_with_an_epoch_expiry() {
        let cookie = delete(SameSite::Lax, false);
        assert!(cookie.starts_with("refreshToken=;"));
        assert!(cookie.contains("expires=Thu, 01 Jan 1970 00:00:00 GMT"));
        assert!(cookie.contains("path=/"));
        assert!(cookie.contains("samesite=lax"));
        assert!(cookie.contains("httponly"));
    }

    #[test]
    fn reading_handles_base64_padding_and_other_cookies() {
        let header = "other=1; refreshToken=YWJjZA==; theme=dark";
        assert_eq!(read(Some(header)).as_deref(), Some("YWJjZA=="));
        assert_eq!(read(Some("theme=dark")), None);
        assert_eq!(read(None), None);
    }

    #[test]
    fn a_persistent_cookie_expiry_is_one_year_away_for_remember_me() {
        // Garde-fou du test d'intégration Login_WithRememberMe_SetsPersistentCookieForAYear.
        let expires = Utc::now() + Duration::days(365);
        let cookie = build("abc", Some(expires), SameSite::Lax, false);
        assert!(cookie.contains(&format_expires(expires)));
    }
}
