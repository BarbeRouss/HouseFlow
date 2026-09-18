//! Génération et validation des clés API (portage de `ApiKeyService`).
//!
//! Format : `hf_` + 32 caractères base62. `Prefix` = les 11 premiers caractères,
//! `KeyHash` = SHA-256 hexadécimal minuscule de la clé complète.

use chrono::{DateTime, Utc};
use rand::rngs::OsRng;
use rand::RngCore;
use sha2::{Digest, Sha256};
use sqlx::PgPool;
use uuid::Uuid;

use crate::audit::{self, AuditContext};
use crate::clock;
use crate::error::AppResult;
use crate::models::ApiKeyScope;

pub const KEY_PREFIX: &str = "hf_";
pub const PREFIX_LENGTH: usize = 11;
pub const RANDOM_BYTES_LENGTH: usize = 32;
pub const MAX_KEYS_PER_USER: i64 = 5;

const BASE62: &[u8] = b"0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

/// Clé fraîchement générée, avant persistance.
#[derive(Debug, Clone)]
pub struct GeneratedKey {
    /// Clé complète, montrée une seule fois à l'utilisateur.
    pub full_key: String,
    pub prefix: String,
    pub key_hash: String,
}

/// Tire une clé API : `hf_` + 32 caractères base62 issus de 32 octets aléatoires.
pub fn generate_key() -> GeneratedKey {
    let mut bytes = [0u8; RANDOM_BYTES_LENGTH];
    OsRng.fill_bytes(&mut bytes);

    let body: String = bytes
        .iter()
        .map(|b| BASE62[(*b as usize) % BASE62.len()] as char)
        .collect();

    let full_key = format!("{KEY_PREFIX}{body}");
    let prefix = full_key[..PREFIX_LENGTH].to_string();
    let key_hash = sha256_hex(&full_key);

    GeneratedKey {
        full_key,
        prefix,
        key_hash,
    }
}

/// SHA-256 hexadécimal minuscule (`Convert.ToHexStringLower` du C#).
pub fn sha256_hex(input: &str) -> String {
    let digest = Sha256::digest(input.as_bytes());
    hex::encode(digest)
}

/// Identité portée par une clé API valide.
#[derive(Debug, Clone, Copy)]
pub struct ApiKeyIdentity {
    pub user_id: Uuid,
    pub scope: ApiKeyScope,
}

/// Valide une clé brute et met à jour `LastUsedAt`, comme `ApiKeyService.ValidateKeyAsync`.
///
/// Le `SaveChangesAsync` du C# journalise cette modification ; comme la validation a
/// lieu avant le middleware d'audit, la ligne porte un contexte anonyme.
pub async fn validate_key(pool: &PgPool, raw_key: &str) -> AppResult<Option<ApiKeyIdentity>> {
    if raw_key.is_empty() || !raw_key.starts_with(KEY_PREFIX) || raw_key.len() < PREFIX_LENGTH {
        return Ok(None);
    }

    let prefix = &raw_key[..PREFIX_LENGTH];
    let key_hash = sha256_hex(raw_key);
    let now = clock::now();

    // La lecture de l'ancienne valeur et la mise à jour tiennent en une requête : la
    // clé n'est retournée que si elle correspond et n'est pas révoquée.
    let row: Option<(Uuid, Uuid, String, Option<DateTime<Utc>>)> = sqlx::query_as(
        r#"WITH matched AS (
               SELECT "Id", "LastUsedAt"
                 FROM "ApiKeys"
                WHERE "Prefix" = $1 AND "KeyHash" = $2 AND "RevokedAt" IS NULL
           )
           UPDATE "ApiKeys" AS k
              SET "LastUsedAt" = $3
             FROM matched
            WHERE k."Id" = matched."Id"
        RETURNING k."Id", k."UserId", k."Scope", matched."LastUsedAt""#,
    )
    .bind(prefix)
    .bind(&key_hash)
    .bind(now)
    .fetch_optional(pool)
    .await?;

    let Some((key_id, user_id, scope, previous_last_used)) = row else {
        return Ok(None);
    };

    audit::record_modified_standalone(
        pool,
        &AuditContext::anonymous(),
        "ApiKey",
        key_id,
        &audit::values([("LastUsedAt", audit::opt_date(previous_last_used))]),
        &audit::values([("LastUsedAt", audit::date(now))]),
        &["LastUsedAt"],
    )
    .await?;

    Ok(scope
        .parse::<ApiKeyScope>()
        .ok()
        .map(|scope| ApiKeyIdentity { user_id, scope }))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn generated_keys_have_the_dotnet_shape() {
        let key = generate_key();
        assert!(key.full_key.starts_with("hf_"));
        assert_eq!(key.full_key.len(), KEY_PREFIX.len() + RANDOM_BYTES_LENGTH);
        assert_eq!(key.prefix.len(), PREFIX_LENGTH);
        assert!(key.full_key.starts_with(&key.prefix));
        assert_eq!(key.key_hash.len(), 64);
        assert!(key.key_hash.chars().all(|c| c.is_ascii_hexdigit()));
        assert_eq!(key.key_hash, key.key_hash.to_lowercase());
        assert!(key.full_key[3..].chars().all(|c| c.is_ascii_alphanumeric()));
    }

    #[test]
    fn generated_keys_are_distinct() {
        assert_ne!(generate_key().full_key, generate_key().full_key);
    }

    #[test]
    fn sha256_matches_the_reference_vector() {
        // Même valeur que Convert.ToHexStringLower(SHA256.HashData(UTF8("abc"))).
        assert_eq!(
            sha256_hex("abc"),
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"
        );
    }
}
