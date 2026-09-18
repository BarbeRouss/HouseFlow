//! Clés API — portage de `ApiKeyService` (création, liste, révocation).
//!
//! La validation d'une clé entrante vit dans [`crate::auth::api_key`], côté
//! authentification.

use serde_json::Value;
use sqlx::PgPool;
use uuid::Uuid;

use crate::audit::{self, AuditContext};
use crate::auth::api_key::{self, MAX_KEYS_PER_USER};
use crate::clock;
use crate::dto::api_keys::{ApiKeyDto, CreateApiKeyRequest, CreateApiKeyResponse};
use crate::error::{AppError, AppResult};
use crate::models::{ApiKey, ApiKeyScope};

/// Crée une clé API. La valeur en clair n'est renvoyée qu'ici, une seule fois.
pub async fn create(
    pool: &PgPool,
    context: &AuditContext,
    user_id: Uuid,
    request: &CreateApiKeyRequest,
    ip_address: Option<&str>,
) -> AppResult<CreateApiKeyResponse> {
    let scope: ApiKeyScope = request.scope().parse().map_err(|_| {
        AppError::BadRequest("Invalid scope. Must be 'ReadOnly' or 'ReadWrite'.".to_string())
    })?;

    let mut tx = pool.begin().await?;

    let (active_count,): (i64,) = sqlx::query_as(
        r#"SELECT COUNT(*) FROM "ApiKeys" WHERE "UserId" = $1 AND "RevokedAt" IS NULL"#,
    )
    .bind(user_id)
    .fetch_one(&mut *tx)
    .await?;

    if active_count >= MAX_KEYS_PER_USER {
        return Err(AppError::BadRequest(format!(
            "Maximum of {MAX_KEYS_PER_USER} active API keys allowed. Revoke an existing key before creating a new one."
        )));
    }

    let generated = api_key::generate_key();
    let now = clock::now();
    let key = ApiKey {
        id: Uuid::new_v4(),
        user_id,
        name: request.name().to_string(),
        prefix: generated.prefix.clone(),
        key_hash: generated.key_hash.clone(),
        scope,
        created_at: now,
        created_by_ip: ip_address.map(str::to_string),
        last_used_at: None,
        revoked_at: None,
    };

    sqlx::query(
        r#"INSERT INTO "ApiKeys"
               ("Id", "UserId", "Name", "Prefix", "KeyHash", "Scope", "CreatedAt", "CreatedByIp")
           VALUES ($1, $2, $3, $4, $5, $6, $7, $8)"#,
    )
    .bind(key.id)
    .bind(key.user_id)
    .bind(&key.name)
    .bind(&key.prefix)
    .bind(&key.key_hash)
    .bind(key.scope.to_string())
    .bind(key.created_at)
    .bind(key.created_by_ip.as_deref())
    .execute(&mut *tx)
    .await?;

    audit::record_added(
        &mut tx,
        context,
        "ApiKey",
        key.id,
        &audit::values([
            ("Id", audit::id(key.id)),
            ("UserId", audit::id(key.user_id)),
            ("Name", Value::String(key.name.clone())),
            ("Prefix", Value::String(key.prefix.clone())),
            ("KeyHash", Value::String(key.key_hash.clone())),
            ("Scope", Value::String(key.scope.to_string())),
            ("CreatedAt", audit::date(key.created_at)),
            ("CreatedByIp", audit::opt_str(key.created_by_ip.as_deref())),
            ("LastUsedAt", Value::Null),
            ("RevokedAt", Value::Null),
        ]),
    )
    .await?;

    tx.commit().await?;

    tracing::info!(user_id = %user_id, prefix = %key.prefix, "API key created");

    Ok(CreateApiKeyResponse {
        id: key.id,
        name: key.name,
        key: generated.full_key,
        prefix: key.prefix,
        scope: scope.to_string(),
        created_at: key.created_at,
    })
}

/// Liste les clés actives, de la plus récente à la plus ancienne.
pub async fn list(pool: &PgPool, user_id: Uuid) -> AppResult<Vec<ApiKeyDto>> {
    let keys: Vec<ApiKey> = sqlx::query_as(
        r#"SELECT * FROM "ApiKeys"
            WHERE "UserId" = $1 AND "RevokedAt" IS NULL
            ORDER BY "CreatedAt" DESC"#,
    )
    .bind(user_id)
    .fetch_all(pool)
    .await?;

    Ok(keys.iter().map(ApiKeyDto::from).collect())
}

/// Révoque une clé appartenant à l'utilisateur.
pub async fn revoke(
    pool: &PgPool,
    context: &AuditContext,
    user_id: Uuid,
    key_id: Uuid,
) -> AppResult<()> {
    let mut tx = pool.begin().await?;

    let key: Option<ApiKey> =
        sqlx::query_as(r#"SELECT * FROM "ApiKeys" WHERE "Id" = $1 AND "UserId" = $2 FOR UPDATE"#)
            .bind(key_id)
            .bind(user_id)
            .fetch_optional(&mut *tx)
            .await?;

    // KeyNotFoundException ⇒ 404 ; clé déjà révoquée ⇒ InvalidOperationException ⇒ 400.
    let key = key.ok_or_else(|| AppError::NotFound("API key not found.".to_string()))?;
    if key.revoked_at.is_some() {
        return Err(AppError::BadRequest(
            "API key is already revoked.".to_string(),
        ));
    }

    let now = clock::now();
    sqlx::query(r#"UPDATE "ApiKeys" SET "RevokedAt" = $1 WHERE "Id" = $2"#)
        .bind(now)
        .bind(key_id)
        .execute(&mut *tx)
        .await?;

    audit::record_modified(
        &mut tx,
        context,
        "ApiKey",
        key_id,
        &audit::values([("RevokedAt", Value::Null)]),
        &audit::values([("RevokedAt", audit::date(now))]),
        &["RevokedAt"],
    )
    .await?;

    tx.commit().await?;

    tracing::info!(user_id = %user_id, prefix = %key.prefix, "API key revoked");
    Ok(())
}
