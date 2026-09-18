//! Configuration du serveur, entièrement pilotée par variables d'environnement.
//!
//! Équivalent des `appsettings*.json` + variables `__` du backend .NET : les noms
//! d'origine (`Jwt:Key` → `JWT__KEY`) sont conservés pour que les deux backends se
//! lancent avec le même environnement.

use std::env;

/// Valeur `SameSite` du cookie de refresh token.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SameSite {
    Lax,
    Strict,
    None,
}

impl SameSite {
    /// Rendu identique à ASP.NET Core (`samesite=lax`), en minuscules.
    pub fn as_attribute(self) -> &'static str {
        match self {
            SameSite::Lax => "lax",
            SameSite::Strict => "strict",
            SameSite::None => "none",
        }
    }

    fn parse(value: &str) -> Option<Self> {
        match value.trim().to_ascii_lowercase().as_str() {
            "lax" => Some(SameSite::Lax),
            "strict" => Some(SameSite::Strict),
            "none" => Some(SameSite::None),
            _ => None,
        }
    }
}

#[derive(Debug, Clone)]
pub struct Config {
    pub database_url: String,
    pub port: u16,
    pub app_env: String,
    pub auto_migrate: bool,
    pub jwt_key: String,
    pub jwt_issuer: String,
    pub jwt_audience: String,
    /// Comptes promus administrateurs au démarrage / à l'inscription.
    pub bootstrap_emails: Vec<String>,
    pub demo_mode: bool,
    /// `None` = toutes les origines autorisées (`CORS__ORIGINS=*`).
    pub cors_origins: Option<Vec<String>>,
    pub cookie_same_site: SameSite,
}

/// Compte de démonstration semé quand `DEMO_MODE=true` (cf. `AdminBootstrap.DemoEmail`).
pub const DEMO_EMAIL: &str = "demo@demo.com";
/// Nom du rôle administrateur porté par le JWT (cf. `AdminBootstrap.AdminRole`).
pub const ADMIN_ROLE: &str = "Admin";

fn var(name: &str) -> Option<String> {
    match env::var(name) {
        Ok(v) if !v.trim().is_empty() => Some(v),
        _ => None,
    }
}

/// Lit une variable en acceptant les deux orthographes utilisées par .NET
/// (`AUTH__COOKIE_SAME_SITE` et `Auth__CookieSameSite`).
fn var_any(names: &[&str]) -> Option<String> {
    names.iter().find_map(|n| var(n))
}

impl Config {
    pub fn from_env() -> anyhow::Result<Self> {
        let database_url = var("DATABASE_URL").unwrap_or_else(|| {
            let host = var("POSTGRES_HOST").unwrap_or_else(|| "localhost".to_string());
            format!("postgres://postgres:postgres@{host}:5432/houseflow_rust")
        });

        let port = var("PORT")
            .unwrap_or_else(|| "5204".to_string())
            .parse::<u16>()
            .map_err(|_| anyhow::anyhow!("PORT must be a valid port number"))?;

        let app_env = var("APP_ENV").unwrap_or_else(|| "Development".to_string());

        let auto_migrate = var("AUTO_MIGRATE")
            .map(|v| v.eq_ignore_ascii_case("true"))
            .unwrap_or(true);

        let jwt_key = var("JWT__KEY")
            .or_else(|| var("Jwt__Key"))
            .ok_or_else(|| anyhow::anyhow!("JWT Key not configured. Set JWT__KEY."))?;
        if jwt_key.len() < 32 {
            anyhow::bail!("JWT Key must be at least 32 characters (256 bits) for security.");
        }

        let demo_mode = var("DEMO_MODE")
            .map(|v| v.eq_ignore_ascii_case("true"))
            .unwrap_or(false);

        Ok(Self {
            database_url,
            port,
            app_env,
            auto_migrate,
            jwt_key,
            jwt_issuer: var_any(&["JWT__ISSUER", "Jwt__Issuer"])
                .unwrap_or_else(|| "HouseFlowAPI".to_string()),
            jwt_audience: var_any(&["JWT__AUDIENCE", "Jwt__Audience"])
                .unwrap_or_else(|| "HouseFlowClient".to_string()),
            bootstrap_emails: bootstrap_emails_from_env(demo_mode),
            demo_mode,
            cors_origins: cors_origins_from_env(),
            cookie_same_site: var_any(&["AUTH__COOKIE_SAME_SITE", "Auth__CookieSameSite"])
                .and_then(|v| SameSite::parse(&v))
                .unwrap_or(SameSite::Lax),
        })
    }

    pub fn is_development(&self) -> bool {
        self.app_env.eq_ignore_ascii_case("Development")
    }

    /// Un compte listé dans `ADMIN__BOOTSTRAP_EMAILS` devient admin à l'inscription.
    pub fn is_bootstrap_admin(&self, email: &str) -> bool {
        self.bootstrap_emails
            .iter()
            .any(|e| e.eq_ignore_ascii_case(email))
    }
}

/// Liste des e-mails d'administrateurs d'amorçage, plus le compte de démo en `DEMO_MODE`.
///
/// Accepte la liste séparée par virgules (`ADMIN__BOOTSTRAP_EMAILS`) et la forme
/// indexée de .NET (`Admin__BootstrapEmails__0`, `__1`, …).
fn bootstrap_emails_from_env(demo_mode: bool) -> Vec<String> {
    let mut emails: Vec<String> = var_any(&["ADMIN__BOOTSTRAP_EMAILS", "Admin__BootstrapEmails"])
        .map(|raw| parse_email_list(&raw))
        .unwrap_or_else(|| vec!["julienrousselle@outlook.be".to_string()]);

    for index in 0.. {
        match var(&format!("Admin__BootstrapEmails__{index}"))
            .or_else(|| var(&format!("ADMIN__BOOTSTRAP_EMAILS__{index}")))
        {
            Some(value) => {
                let value = value.trim().to_string();
                if !value.is_empty() && !emails.iter().any(|e| e.eq_ignore_ascii_case(&value)) {
                    emails.push(value);
                }
            }
            None => break,
        }
    }

    if demo_mode && !emails.iter().any(|e| e.eq_ignore_ascii_case(DEMO_EMAIL)) {
        emails.push(DEMO_EMAIL.to_string());
    }

    emails
}

/// Découpe une liste séparée par virgules en supprimant blancs et entrées vides.
pub fn parse_email_list(raw: &str) -> Vec<String> {
    raw.split(',')
        .map(str::trim)
        .filter(|s| !s.is_empty())
        .map(str::to_string)
        .collect()
}

fn cors_origins_from_env() -> Option<Vec<String>> {
    let raw = var("CORS__ORIGINS")
        .unwrap_or_else(|| "http://localhost:3000,https://localhost:3000".to_string());
    let origins = parse_email_list(&raw);
    if origins.len() == 1 && origins[0] == "*" {
        None
    } else {
        Some(origins)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    // Portage de AdminBootstrapTests : liste nettoyée, casse ignorée, démo non dupliquée.

    #[test]
    fn parse_email_list_trims_and_drops_blanks() {
        assert_eq!(
            parse_email_list(" julienrousselle@outlook.be , ,"),
            vec!["julienrousselle@outlook.be".to_string()]
        );
    }

    #[test]
    fn parse_email_list_without_entries_is_empty() {
        assert!(parse_email_list("").is_empty());
        assert!(parse_email_list("  ,  ").is_empty());
    }

    fn config_with(emails: Vec<String>) -> Config {
        Config {
            database_url: String::new(),
            port: 0,
            app_env: "Development".into(),
            auto_migrate: false,
            jwt_key: "x".repeat(32),
            jwt_issuer: "HouseFlowAPI".into(),
            jwt_audience: "HouseFlowClient".into(),
            bootstrap_emails: emails,
            demo_mode: false,
            cors_origins: None,
            cookie_same_site: SameSite::Lax,
        }
    }

    #[test]
    fn is_bootstrap_admin_is_case_insensitive() {
        let config = config_with(vec!["julienrousselle@outlook.be".into()]);
        assert!(config.is_bootstrap_admin("JulienRousselle@Outlook.be"));
        assert!(!config.is_bootstrap_admin("someone@example.com"));
    }

    #[test]
    fn demo_account_is_bootstrap_admin_only_in_demo_mode() {
        let with_demo = config_with(vec![DEMO_EMAIL.to_string()]);
        assert!(with_demo.is_bootstrap_admin(DEMO_EMAIL));

        let without_demo = config_with(vec![]);
        assert!(!without_demo.is_bootstrap_admin(DEMO_EMAIL));
    }

    #[test]
    fn same_site_parsing_is_case_insensitive_and_falls_back() {
        assert_eq!(SameSite::parse("None"), Some(SameSite::None));
        assert_eq!(SameSite::parse("STRICT"), Some(SameSite::Strict));
        assert_eq!(SameSite::parse("nope"), None);
        assert_eq!(SameSite::Lax.as_attribute(), "lax");
    }
}
