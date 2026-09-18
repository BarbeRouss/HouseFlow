//! Émission et vérification des JWT d'accès.
//!
//! Claims identiques à `AuthService.GenerateJwtToken` : `sub`, `email`, `jti`, `iss`,
//! `aud`, `exp` (15 minutes) et, pour les administrateurs, le claim de rôle
//! Microsoft. `ClockSkew = 0` côté validation. Un jeton émis par l'un des deux
//! backends est accepté par l'autre dès lors que la clé est la même.

use chrono::Utc;
use jsonwebtoken::{decode, encode, Algorithm, DecodingKey, EncodingKey, Header, Validation};
use serde::{Deserialize, Serialize};
use uuid::Uuid;

use crate::config::{Config, ADMIN_ROLE};

/// URI du claim de rôle utilisé par `System.Security.Claims.ClaimTypes.Role`.
pub const ROLE_CLAIM: &str = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";

/// Durée de vie de l'access token, en secondes (15 minutes, comme .NET).
pub const ACCESS_TOKEN_LIFETIME_SECONDS: i64 = 900;

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Claims {
    pub sub: String,
    pub email: String,
    pub jti: String,
    #[serde(skip_serializing_if = "Option::is_none", default)]
    #[serde(rename = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role")]
    pub role: Option<String>,
    pub iss: String,
    pub aud: String,
    pub exp: i64,
    pub nbf: i64,
    pub iat: i64,
}

impl Claims {
    pub fn is_admin(&self) -> bool {
        self.role.as_deref() == Some(ADMIN_ROLE)
    }
}

/// Génère l'access token d'un utilisateur. Le rôle admin ne voyage que par JWT.
pub fn generate_token(config: &Config, user_id: Uuid, email: &str, is_admin: bool) -> String {
    let now = Utc::now().timestamp();
    let claims = Claims {
        sub: user_id.to_string(),
        email: email.to_string(),
        jti: Uuid::new_v4().to_string(),
        role: is_admin.then(|| ADMIN_ROLE.to_string()),
        iss: config.jwt_issuer.clone(),
        aud: config.jwt_audience.clone(),
        exp: now + ACCESS_TOKEN_LIFETIME_SECONDS,
        nbf: now,
        iat: now,
    };

    encode(
        &Header::new(Algorithm::HS256),
        &claims,
        &EncodingKey::from_secret(config.jwt_key.as_bytes()),
    )
    .expect("HS256 signing with a static key cannot fail")
}

/// Valide un access token (signature, émetteur, audience, expiration sans tolérance).
pub fn verify_token(config: &Config, token: &str) -> Option<Claims> {
    let mut validation = Validation::new(Algorithm::HS256);
    validation.set_issuer(&[config.jwt_issuer.as_str()]);
    validation.set_audience(&[config.jwt_audience.as_str()]);
    validation.leeway = 0; // ClockSkew = TimeSpan.Zero
    validation.validate_exp = true;
    validation.validate_nbf = true;

    decode::<Claims>(
        token,
        &DecodingKey::from_secret(config.jwt_key.as_bytes()),
        &validation,
    )
    .ok()
    .map(|data| data.claims)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::config::SameSite;

    fn config() -> Config {
        Config {
            database_url: String::new(),
            port: 0,
            app_env: "Development".into(),
            auto_migrate: false,
            jwt_key: "DevOnlySecretKey_DO_NOT_USE_IN_PRODUCTION_MinimumLengthRequired256Bits!"
                .into(),
            jwt_issuer: "HouseFlowAPI".into(),
            jwt_audience: "HouseFlowClient".into(),
            bootstrap_emails: vec![],
            demo_mode: false,
            cors_origins: None,
            cookie_same_site: SameSite::Lax,
        }
    }

    #[test]
    fn generated_token_carries_the_expected_claims() {
        let config = config();
        let id = Uuid::new_v4();
        let token = generate_token(&config, id, "user@example.com", false);

        // Trois segments compacts, comme tout JWT.
        assert_eq!(token.split('.').count(), 3);

        let claims = verify_token(&config, &token).expect("token should validate");
        assert_eq!(claims.sub, id.to_string());
        assert_eq!(claims.email, "user@example.com");
        assert_eq!(claims.iss, "HouseFlowAPI");
        assert_eq!(claims.aud, "HouseFlowClient");
        assert!(claims.role.is_none());
        assert!(!claims.is_admin());
        assert!(Uuid::parse_str(&claims.jti).is_ok());
        assert_eq!(claims.exp - claims.iat, ACCESS_TOKEN_LIFETIME_SECONDS);
    }

    #[test]
    fn admin_tokens_carry_the_microsoft_role_claim() {
        let config = config();
        let token = generate_token(&config, Uuid::new_v4(), "admin@example.com", true);
        let claims = verify_token(&config, &token).unwrap();
        assert_eq!(claims.role.as_deref(), Some("Admin"));
        assert!(claims.is_admin());

        // Le claim est bien émis sous l'URI Microsoft attendue par le backend .NET.
        let payload = token.split('.').nth(1).unwrap();
        use base64::Engine;
        let decoded = base64::engine::general_purpose::URL_SAFE_NO_PAD
            .decode(payload)
            .unwrap();
        let json: serde_json::Value = serde_json::from_slice(&decoded).unwrap();
        assert_eq!(json[ROLE_CLAIM], "Admin");
    }

    #[test]
    fn a_token_signed_with_another_key_is_rejected() {
        let token = generate_token(&config(), Uuid::new_v4(), "user@example.com", false);
        let mut other = config();
        other.jwt_key = "AnotherSecretKeyThatIsAtLeastThirtyTwoChars!".into();
        assert!(verify_token(&other, &token).is_none());
    }

    #[test]
    fn a_token_for_another_audience_is_rejected() {
        let token = generate_token(&config(), Uuid::new_v4(), "user@example.com", false);
        let mut other = config();
        other.jwt_audience = "SomeOtherClient".into();
        assert!(verify_token(&other, &token).is_none());
    }

    #[test]
    fn garbage_is_not_a_token() {
        assert!(verify_token(&config(), "not-a-token").is_none());
        assert!(verify_token(&config(), "").is_none());
    }
}
