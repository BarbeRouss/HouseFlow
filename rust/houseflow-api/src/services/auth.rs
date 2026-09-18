//! Service d'authentification — portage de `AuthService`.
//!
//! Rotation des refresh tokens : chaque connexion ouvre une **famille** de jetons ;
//! un refresh remplace le jeton courant en restant dans la famille. Rejouer un jeton
//! déjà tourné est toléré pendant 30 secondes (deux onglets qui démarrent ensemble),
//! au-delà c'est traité comme un vol et toute la famille est révoquée.

use base64::Engine;
use chrono::{DateTime, Duration, Utc};
use rand::rngs::OsRng;
use rand::RngCore;
use serde_json::Value;
use sqlx::{PgPool, Postgres, Transaction};
use uuid::Uuid;

use crate::audit::{self, AuditContext};
use crate::auth::{jwt, password};
use crate::clock;
use crate::config::Config;
use crate::dto::auth::{AuthOutcome, AuthResponse, LoginRequest, RegisterRequest, UserDto};
use crate::error::{AppError, AppResult};
use crate::models::{HouseRole, InvitationStatus, RefreshToken, User};

/// Durée de vie d'une session « se souvenir de moi » (glissante).
pub const REMEMBER_ME_LIFETIME_DAYS: i64 = 365;
/// Durée de vie d'une session simple (le cookie meurt avec le navigateur).
pub const SESSION_LIFETIME_HOURS: i64 = 24;
/// Nombre maximum de sessions simultanées ; la moins récente est évincée.
pub const MAX_SESSIONS_PER_USER: usize = 10;
/// Fenêtre pendant laquelle un jeton déjà tourné est encore honoré.
pub const ROTATION_GRACE_SECONDS: i64 = 30;
/// Durée de conservation des jetons révoqués/expirés (détection de réutilisation).
pub const REVOKED_TOKEN_RETENTION_DAYS: i64 = 7;

/// Message unique des échecs de connexion (aucune distinction e-mail/mot de passe).
const INVALID_CREDENTIALS: &str = "Invalid email or password";
/// Message unique des échecs de refresh.
const INVALID_REFRESH_TOKEN: &str = "Invalid or expired refresh token";

fn remember_me_lifetime() -> Duration {
    Duration::days(REMEMBER_ME_LIFETIME_DAYS)
}

fn session_lifetime() -> Duration {
    Duration::hours(SESSION_LIFETIME_HOURS)
}

/// Jeton aléatoire de 64 octets encodé en base64 (identique à `Convert.ToBase64String`).
fn new_token_value() -> String {
    let mut bytes = [0u8; 64];
    OsRng.fill_bytes(&mut bytes);
    base64::engine::general_purpose::STANDARD.encode(bytes)
}

// --- Inscription -----------------------------------------------------------

/// Inscrit un utilisateur, lui crée sa maison « Ma maison » et ouvre une session.
///
/// Une inscription n'est jamais une session « se souvenir de moi ».
pub async fn register(
    pool: &PgPool,
    config: &Config,
    request: &RegisterRequest,
    ip_address: Option<&str>,
    invitation_token: Option<&str>,
) -> AppResult<AuthOutcome> {
    let email = request.email();

    // Le contexte d'audit de cette requête est l'e-mail d'inscription : aucun JWT
    // n'existe encore (SetAuditContext(null, request.Email, ipAddress)).
    let context = AuditContext::overridden(
        None,
        Some(email.to_string()),
        ip_address.map(str::to_string),
    );

    let mut tx = pool.begin().await?;

    let existing: Option<(i32,)> = sqlx::query_as(r#"SELECT 1 FROM "Users" WHERE "Email" = $1"#)
        .bind(email)
        .fetch_optional(&mut *tx)
        .await?;
    if existing.is_some() {
        return Err(already_registered());
    }

    let now = clock::now();
    let user = User {
        id: Uuid::new_v4(),
        email: email.to_string(),
        password_hash: password::hash(request.password()),
        created_at: now,
        updated_at: None,
        first_name: request.first_name().to_string(),
        last_name: request.last_name().to_string(),
        language: "fr".to_string(),
        theme: "system".to_string(),
        is_admin: config.is_bootstrap_admin(email),
    };

    insert_user(&mut tx, &context, &user).await?;

    // Maison par défaut + appartenance Owner.
    let house_id = Uuid::new_v4();
    insert_default_house(&mut tx, &context, house_id, user.id, now).await?;
    insert_membership(&mut tx, &context, user.id, house_id, HouseRole::Owner, now).await?;

    // Invitation éventuelle : le nouvel utilisateur rejoint aussi la maison invitante.
    if let Some(token) = invitation_token.filter(|t| !t.is_empty()) {
        accept_invitation_at_registration(&mut tx, &context, token, user.id, now).await?;
    }

    let refresh_token = start_session(&mut tx, &context, user.id, ip_address, false).await?;

    tx.commit().await?;

    tracing::info!(user_id = %user.id, email = %user.email, "user registered successfully");
    Ok(build_auth_outcome(config, &user, &refresh_token))
}

fn already_registered() -> AppError {
    AppError::Conflict(
        "This email address is already registered. Please use a different email or try logging in."
            .to_string(),
    )
}

async fn insert_user(
    tx: &mut Transaction<'_, Postgres>,
    context: &AuditContext,
    user: &User,
) -> AppResult<()> {
    sqlx::query(
        r#"INSERT INTO "Users"
               ("Id", "Email", "PasswordHash", "CreatedAt", "UpdatedAt",
                "FirstName", "LastName", "Language", "Theme", "IsAdmin")
           VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)"#,
    )
    .bind(user.id)
    .bind(&user.email)
    .bind(&user.password_hash)
    .bind(user.created_at)
    .bind(user.updated_at)
    .bind(&user.first_name)
    .bind(&user.last_name)
    .bind(&user.language)
    .bind(&user.theme)
    .bind(user.is_admin)
    .execute(&mut **tx)
    .await
    .map_err(|error| match error {
        // Course entre deux inscriptions simultanées : l'index unique tranche.
        sqlx::Error::Database(ref db) if db.constraint() == Some("IX_Users_Email") => {
            already_registered()
        }
        other => other.into(),
    })?;

    audit::record_added(
        tx,
        context,
        "User",
        user.id,
        &audit::values([
            ("Id", audit::id(user.id)),
            ("Email", Value::String(user.email.clone())),
            ("PasswordHash", Value::String(user.password_hash.clone())),
            ("CreatedAt", audit::date(user.created_at)),
            ("UpdatedAt", audit::opt_date(user.updated_at)),
            ("FirstName", Value::String(user.first_name.clone())),
            ("LastName", Value::String(user.last_name.clone())),
            ("Language", Value::String(user.language.clone())),
            ("Theme", Value::String(user.theme.clone())),
            ("IsAdmin", Value::Bool(user.is_admin)),
        ]),
    )
    .await
}

async fn insert_default_house(
    tx: &mut Transaction<'_, Postgres>,
    context: &AuditContext,
    house_id: Uuid,
    user_id: Uuid,
    now: DateTime<Utc>,
) -> AppResult<()> {
    sqlx::query(
        r#"INSERT INTO "Houses" ("Id", "Name", "CreatedAt", "UserId") VALUES ($1, $2, $3, $4)"#,
    )
    .bind(house_id)
    .bind("Ma maison")
    .bind(now)
    .bind(user_id)
    .execute(&mut **tx)
    .await?;

    audit::record_added(
        tx,
        context,
        "House",
        house_id,
        &audit::values([
            ("Id", audit::id(house_id)),
            ("Name", Value::String("Ma maison".to_string())),
            ("Address", Value::Null),
            ("ZipCode", Value::Null),
            ("City", Value::Null),
            ("Country", Value::Null),
            ("CreatedAt", audit::date(now)),
            ("UpdatedAt", Value::Null),
            ("UserId", audit::id(user_id)),
        ]),
    )
    .await
}

async fn insert_membership(
    tx: &mut Transaction<'_, Postgres>,
    context: &AuditContext,
    user_id: Uuid,
    house_id: Uuid,
    role: HouseRole,
    now: DateTime<Utc>,
) -> AppResult<()> {
    let member_id = Uuid::new_v4();
    sqlx::query(
        r#"INSERT INTO "HouseMembers"
               ("Id", "Role", "CanLogMaintenance", "CreatedAt", "UserId", "HouseId", "CanViewCosts")
           VALUES ($1, $2, true, $3, $4, $5, false)"#,
    )
    .bind(member_id)
    .bind(role.to_string())
    .bind(now)
    .bind(user_id)
    .bind(house_id)
    .execute(&mut **tx)
    .await?;

    audit::record_added(
        tx,
        context,
        "HouseMember",
        member_id,
        &audit::values([
            ("Id", audit::id(member_id)),
            ("Role", Value::String(role.to_string())),
            ("CanLogMaintenance", Value::Bool(true)),
            ("CanViewCosts", Value::Bool(false)),
            ("CreatedAt", audit::date(now)),
            ("UpdatedAt", Value::Null),
            ("UserId", audit::id(user_id)),
            ("HouseId", audit::id(house_id)),
        ]),
    )
    .await
}

/// Accepte l'invitation passée en `?invitationToken=` si elle est encore valide.
async fn accept_invitation_at_registration(
    tx: &mut Transaction<'_, Postgres>,
    context: &AuditContext,
    token: &str,
    user_id: Uuid,
    now: DateTime<Utc>,
) -> AppResult<()> {
    let invitation: Option<(Uuid, Uuid, String)> = sqlx::query_as(
        r#"SELECT "Id", "HouseId", "Role"
             FROM "Invitations"
            WHERE "Token" = $1 AND "Status" = $2 AND "ExpiresAt" > $3"#,
    )
    .bind(token)
    .bind(InvitationStatus::Pending.to_string())
    .bind(now)
    .fetch_optional(&mut **tx)
    .await?;

    let Some((invitation_id, house_id, role)) = invitation else {
        return Ok(());
    };
    let role = role.parse::<HouseRole>().unwrap_or(HouseRole::Tenant);

    insert_membership(tx, context, user_id, house_id, role, now).await?;

    sqlx::query(
        r#"UPDATE "Invitations"
              SET "Status" = $1, "AcceptedByUserId" = $2, "AcceptedAt" = $3
            WHERE "Id" = $4"#,
    )
    .bind(InvitationStatus::Accepted.to_string())
    .bind(user_id)
    .bind(now)
    .bind(invitation_id)
    .execute(&mut **tx)
    .await?;

    audit::record_modified(
        tx,
        context,
        "Invitation",
        invitation_id,
        &audit::values([
            (
                "Status",
                Value::String(InvitationStatus::Pending.to_string()),
            ),
            ("AcceptedAt", Value::Null),
            ("AcceptedByUserId", Value::Null),
        ]),
        &audit::values([
            (
                "Status",
                Value::String(InvitationStatus::Accepted.to_string()),
            ),
            ("AcceptedAt", audit::date(now)),
            ("AcceptedByUserId", audit::id(user_id)),
        ]),
        &["Status", "AcceptedAt", "AcceptedByUserId"],
    )
    .await
}

// --- Connexion -------------------------------------------------------------

pub async fn login(
    pool: &PgPool,
    config: &Config,
    request: &LoginRequest,
    ip_address: Option<&str>,
) -> AppResult<AuthOutcome> {
    let user: Option<User> = sqlx::query_as(r#"SELECT * FROM "Users" WHERE "Email" = $1"#)
        .bind(request.email())
        .fetch_optional(pool)
        .await?;

    // Même message et même statut que l'utilisateur existe ou non.
    let Some(user) = user else {
        tracing::warn!(email = %request.email(), "login failed - user not found");
        return Err(AppError::Unauthorized(INVALID_CREDENTIALS.to_string()));
    };

    if !password::verify(request.password(), &user.password_hash) {
        tracing::warn!(user_id = %user.id, "login failed - invalid password");
        return Err(AppError::Unauthorized(INVALID_CREDENTIALS.to_string()));
    }

    let context = AuditContext::overridden(
        Some(user.id),
        Some(user.email.clone()),
        ip_address.map(str::to_string),
    );

    let mut tx = pool.begin().await?;
    let refresh_token = start_session(
        &mut tx,
        &context,
        user.id,
        ip_address,
        request.remember_me(),
    )
    .await?;
    tx.commit().await?;

    tracing::info!(user_id = %user.id, "user logged in successfully");
    Ok(build_auth_outcome(config, &user, &refresh_token))
}

// --- Rafraîchissement ------------------------------------------------------

pub async fn refresh(
    pool: &PgPool,
    config: &Config,
    token: &str,
    ip_address: Option<&str>,
) -> AppResult<AuthOutcome> {
    let current: Option<RefreshToken> =
        sqlx::query_as(r#"SELECT * FROM "RefreshTokens" WHERE "Token" = $1"#)
            .bind(token)
            .fetch_optional(pool)
            .await?;

    let Some(current) = current else {
        tracing::warn!("refresh token unknown");
        return Err(AppError::Unauthorized(INVALID_REFRESH_TOKEN.to_string()));
    };

    let user: User = sqlx::query_as(r#"SELECT * FROM "Users" WHERE "Id" = $1"#)
        .bind(current.user_id)
        .fetch_one(pool)
        .await?;

    let context = AuditContext::overridden(
        Some(current.user_id),
        Some(user.email.clone()),
        ip_address.map(str::to_string),
    );

    let now = clock::now();

    if let Some(replaced_by) = current.replaced_by_token.clone() {
        // Ce jeton a déjà été tourné : soit une course bénigne entre deux onglets,
        // soit un cookie volé.
        let replacement: Option<RefreshToken> =
            sqlx::query_as(r#"SELECT * FROM "RefreshTokens" WHERE "Token" = $1"#)
                .bind(&replaced_by)
                .fetch_optional(pool)
                .await?;

        let within_grace = current
            .revoked_at
            .is_some_and(|revoked| now - revoked <= Duration::seconds(ROTATION_GRACE_SECONDS));

        if within_grace {
            if let Some(replacement) = replacement.filter(|r| r.is_active(now)) {
                tracing::info!(
                    user_id = %current.user_id,
                    "rotated refresh token presented within grace period; re-issuing current token"
                );
                return Ok(build_auth_outcome(config, &user, &replacement));
            }
        }

        let mut tx = pool.begin().await?;
        revoke_family(
            &mut tx,
            &context,
            current.family_id,
            ip_address,
            "Reuse detected",
        )
        .await?;
        tx.commit().await?;

        tracing::warn!(
            user_id = %current.user_id,
            family_id = %current.family_id,
            "refresh token reuse detected: family revoked"
        );
        return Err(AppError::Unauthorized(INVALID_REFRESH_TOKEN.to_string()));
    }

    if !current.is_active(now) {
        tracing::warn!(user_id = %current.user_id, "refresh token revoked or expired");
        return Err(AppError::Unauthorized(INVALID_REFRESH_TOKEN.to_string()));
    }

    let mut tx = pool.begin().await?;
    let rotated = rotate(&mut tx, &context, &current, ip_address).await?;
    tx.commit().await?;

    tracing::info!(user_id = %current.user_id, "token refreshed");
    Ok(build_auth_outcome(config, &user, &rotated))
}

// --- Révocation ------------------------------------------------------------

/// Révoque un refresh token. Un jeton inconnu ou déjà inactif est une erreur 400.
pub async fn revoke_token(
    pool: &PgPool,
    context: &AuditContext,
    token: &str,
    ip_address: Option<&str>,
) -> AppResult<()> {
    let current: Option<RefreshToken> =
        sqlx::query_as(r#"SELECT * FROM "RefreshTokens" WHERE "Token" = $1"#)
            .bind(token)
            .fetch_optional(pool)
            .await?;

    let now = clock::now();
    let Some(current) = current.filter(|t| t.is_active(now)) else {
        tracing::warn!("attempted to revoke an invalid or expired token");
        return Err(AppError::BadRequest("Invalid or expired token".to_string()));
    };

    let mut tx = pool.begin().await?;
    mark_revoked(
        &mut tx,
        context,
        &current,
        now,
        ip_address,
        "Revoked by user",
        None,
    )
    .await?;
    tx.commit().await?;

    tracing::info!(user_id = %current.user_id, "refresh token revoked");
    Ok(())
}

// --- Sessions --------------------------------------------------------------

/// Ouvre une session (nouvelle famille), évince les sessions au-delà du maximum et
/// purge les jetons hors rétention.
async fn start_session(
    tx: &mut Transaction<'_, Postgres>,
    context: &AuditContext,
    user_id: Uuid,
    ip_address: Option<&str>,
    remember_me: bool,
) -> AppResult<RefreshToken> {
    let now = clock::now();
    let tokens: Vec<RefreshToken> =
        sqlx::query_as(r#"SELECT * FROM "RefreshTokens" WHERE "UserId" = $1"#)
            .bind(user_id)
            .fetch_all(&mut **tx)
            .await?;

    let retention = Duration::days(REVOKED_TOKEN_RETENTION_DAYS);

    // Entretien : les jetons révoqués/expirés ne servent qu'à détecter une réutilisation.
    let stale = tokens.iter().filter(|t| {
        t.revoked_at
            .is_some_and(|revoked| revoked < now - retention)
            || t.expires_at < now - retention
    });

    // Le CreatedAt d'un jeton actif est la dernière rotation de sa famille, donc sa
    // dernière activité : on garde les MaxSessionsPerUser - 1 familles les plus
    // récentes, plus celle qu'on ouvre.
    let mut active: Vec<&RefreshToken> = tokens.iter().filter(|t| t.is_active(now)).collect();
    active.sort_by(|a, b| b.created_at.cmp(&a.created_at));
    let evicted_families: Vec<Uuid> = active
        .iter()
        .skip(MAX_SESSIONS_PER_USER - 1)
        .map(|t| t.family_id)
        .collect();
    let evicted = tokens
        .iter()
        .filter(|t| evicted_families.contains(&t.family_id));

    let mut to_delete: Vec<&RefreshToken> = stale.chain(evicted).collect();
    to_delete.sort_by_key(|t| t.id);
    to_delete.dedup_by_key(|t| t.id);

    for token in to_delete {
        delete_token(tx, context, token).await?;
    }

    let token = create_token(
        tx,
        context,
        user_id,
        ip_address,
        Uuid::new_v4(),
        remember_me,
        now,
    )
    .await?;

    Ok(token)
}

/// Remplace un jeton par un nouveau dans la même famille (expiration glissante).
async fn rotate(
    tx: &mut Transaction<'_, Postgres>,
    context: &AuditContext,
    current: &RefreshToken,
    ip_address: Option<&str>,
) -> AppResult<RefreshToken> {
    let now = clock::now();
    let replacement = create_token(
        tx,
        context,
        current.user_id,
        ip_address,
        current.family_id,
        current.remember_me,
        now,
    )
    .await?;

    mark_revoked(
        tx,
        context,
        current,
        now,
        ip_address,
        "Replaced by new token",
        Some(&replacement.token),
    )
    .await?;

    Ok(replacement)
}

async fn create_token(
    tx: &mut Transaction<'_, Postgres>,
    context: &AuditContext,
    user_id: Uuid,
    ip_address: Option<&str>,
    family_id: Uuid,
    remember_me: bool,
    now: DateTime<Utc>,
) -> AppResult<RefreshToken> {
    let token = RefreshToken {
        id: Uuid::new_v4(),
        user_id,
        token: new_token_value(),
        expires_at: now
            + if remember_me {
                remember_me_lifetime()
            } else {
                session_lifetime()
            },
        created_at: now,
        created_by_ip: ip_address.map(str::to_string),
        revoked_at: None,
        revoked_by_ip: None,
        replaced_by_token: None,
        reason_revoked: None,
        family_id,
        remember_me,
    };

    sqlx::query(
        r#"INSERT INTO "RefreshTokens"
               ("Id", "UserId", "Token", "ExpiresAt", "CreatedAt", "CreatedByIp", "FamilyId", "RememberMe")
           VALUES ($1, $2, $3, $4, $5, $6, $7, $8)"#,
    )
    .bind(token.id)
    .bind(token.user_id)
    .bind(&token.token)
    .bind(token.expires_at)
    .bind(token.created_at)
    .bind(token.created_by_ip.as_deref())
    .bind(token.family_id)
    .bind(token.remember_me)
    .execute(&mut **tx)
    .await?;

    audit::record_added(tx, context, "RefreshToken", token.id, &token_values(&token)).await?;

    Ok(token)
}

async fn mark_revoked(
    tx: &mut Transaction<'_, Postgres>,
    context: &AuditContext,
    token: &RefreshToken,
    now: DateTime<Utc>,
    ip_address: Option<&str>,
    reason: &str,
    replaced_by: Option<&str>,
) -> AppResult<()> {
    sqlx::query(
        r#"UPDATE "RefreshTokens"
              SET "RevokedAt" = $1, "RevokedByIp" = $2, "ReasonRevoked" = $3,
                  "ReplacedByToken" = COALESCE($4::text, "ReplacedByToken")
            WHERE "Id" = $5"#,
    )
    .bind(now)
    .bind(ip_address)
    .bind(reason)
    .bind(replaced_by)
    .bind(token.id)
    .execute(&mut **tx)
    .await?;

    let mut changed = vec!["RevokedAt", "RevokedByIp", "ReasonRevoked"];
    let mut old = audit::values([
        ("RevokedAt", audit::opt_date(token.revoked_at)),
        (
            "RevokedByIp",
            audit::opt_str(token.revoked_by_ip.as_deref()),
        ),
        (
            "ReasonRevoked",
            audit::opt_str(token.reason_revoked.as_deref()),
        ),
    ]);
    let mut new = audit::values([
        ("RevokedAt", audit::date(now)),
        ("RevokedByIp", audit::opt_str(ip_address)),
        ("ReasonRevoked", Value::String(reason.to_string())),
    ]);
    if let Some(replacement) = replaced_by {
        changed.push("ReplacedByToken");
        old.insert(
            "ReplacedByToken".to_string(),
            audit::opt_str(token.replaced_by_token.as_deref()),
        );
        new.insert(
            "ReplacedByToken".to_string(),
            Value::String(replacement.to_string()),
        );
    }

    audit::record_modified(tx, context, "RefreshToken", token.id, &old, &new, &changed).await
}

/// Révoque tous les jetons encore actifs d'une famille (réutilisation détectée).
async fn revoke_family(
    tx: &mut Transaction<'_, Postgres>,
    context: &AuditContext,
    family_id: Uuid,
    ip_address: Option<&str>,
    reason: &str,
) -> AppResult<()> {
    let tokens: Vec<RefreshToken> = sqlx::query_as(
        r#"SELECT * FROM "RefreshTokens" WHERE "FamilyId" = $1 AND "RevokedAt" IS NULL"#,
    )
    .bind(family_id)
    .fetch_all(&mut **tx)
    .await?;

    let now = clock::now();
    for token in &tokens {
        mark_revoked(tx, context, token, now, ip_address, reason, None).await?;
    }

    Ok(())
}

async fn delete_token(
    tx: &mut Transaction<'_, Postgres>,
    context: &AuditContext,
    token: &RefreshToken,
) -> AppResult<()> {
    sqlx::query(r#"DELETE FROM "RefreshTokens" WHERE "Id" = $1"#)
        .bind(token.id)
        .execute(&mut **tx)
        .await?;

    audit::record_deleted(tx, context, "RefreshToken", token.id, &token_values(token)).await
}

fn token_values(token: &RefreshToken) -> audit::Values {
    audit::values([
        ("Id", audit::id(token.id)),
        ("UserId", audit::id(token.user_id)),
        ("Token", Value::String(token.token.clone())),
        ("FamilyId", audit::id(token.family_id)),
        ("RememberMe", Value::Bool(token.remember_me)),
        ("ExpiresAt", audit::date(token.expires_at)),
        ("CreatedAt", audit::date(token.created_at)),
        (
            "CreatedByIp",
            audit::opt_str(token.created_by_ip.as_deref()),
        ),
        ("RevokedAt", audit::opt_date(token.revoked_at)),
        (
            "RevokedByIp",
            audit::opt_str(token.revoked_by_ip.as_deref()),
        ),
        (
            "ReplacedByToken",
            audit::opt_str(token.replaced_by_token.as_deref()),
        ),
        (
            "ReasonRevoked",
            audit::opt_str(token.reason_revoked.as_deref()),
        ),
    ])
}

/// `BuildAuthResponse` : access token + utilisateur, et les éléments du cookie.
fn build_auth_outcome(config: &Config, user: &User, refresh_token: &RefreshToken) -> AuthOutcome {
    AuthOutcome {
        response: AuthResponse {
            access_token: jwt::generate_token(config, user.id, &user.email, user.is_admin),
            // Le contrôleur .NET blanchit ces deux champs avant de répondre.
            refresh_token: None,
            expires_in: jwt::ACCESS_TOKEN_LIFETIME_SECONDS as i32,
            user: UserDto::from(user),
            refresh_cookie_expires_at: None,
        },
        refresh_token: refresh_token.token.clone(),
        cookie_expires_at: refresh_token
            .remember_me
            .then_some(refresh_token.expires_at),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn token(
        remember_me: bool,
        created_at: DateTime<Utc>,
        expires_at: DateTime<Utc>,
    ) -> RefreshToken {
        RefreshToken {
            id: Uuid::new_v4(),
            user_id: Uuid::new_v4(),
            token: new_token_value(),
            expires_at,
            created_at,
            created_by_ip: None,
            revoked_at: None,
            revoked_by_ip: None,
            replaced_by_token: None,
            reason_revoked: None,
            family_id: Uuid::new_v4(),
            remember_me,
        }
    }

    #[test]
    fn generated_tokens_are_64_random_bytes_in_base64() {
        let value = new_token_value();
        // 64 octets en base64 standard : 88 caractères avec le remplissage.
        assert_eq!(value.len(), 88);
        assert!(value.ends_with("=="));
        assert_ne!(value, new_token_value());
        let decoded = base64::engine::general_purpose::STANDARD
            .decode(&value)
            .unwrap();
        assert_eq!(decoded.len(), 64);
        // Tient dans la colonne varchar(500).
        assert!(value.len() <= 500);
    }

    #[test]
    fn lifetimes_match_the_csharp_constants() {
        assert_eq!(remember_me_lifetime(), Duration::days(365));
        assert_eq!(session_lifetime(), Duration::hours(24));
        assert_eq!(MAX_SESSIONS_PER_USER, 10);
        assert_eq!(ROTATION_GRACE_SECONDS, 30);
        assert_eq!(REVOKED_TOKEN_RETENTION_DAYS, 7);
    }

    #[test]
    fn an_active_token_is_neither_revoked_nor_expired() {
        let now = Utc::now();
        let mut valid = token(false, now, now + Duration::hours(1));
        assert!(valid.is_active(now));

        valid.revoked_at = Some(now);
        assert!(!valid.is_active(now));

        let expired = token(false, now - Duration::days(2), now - Duration::hours(1));
        assert!(!expired.is_active(now));
        assert!(expired.is_expired(now));
    }

    #[test]
    fn remember_me_drives_the_persistent_cookie_expiry() {
        let now = Utc::now();
        let persistent = token(true, now, now + remember_me_lifetime());
        let session = token(false, now, now + session_lifetime());

        assert_eq!(
            persistent.remember_me.then_some(persistent.expires_at),
            Some(persistent.expires_at)
        );
        assert_eq!(session.remember_me.then_some(session.expires_at), None);
    }

    /// Règle d'éviction de `StartSessionAsync`, isolée de la base : les familles
    /// actives au-delà des 9 plus récentes sont évincées.
    fn evicted_families(tokens: &[RefreshToken], now: DateTime<Utc>) -> Vec<Uuid> {
        let mut active: Vec<&RefreshToken> = tokens.iter().filter(|t| t.is_active(now)).collect();
        active.sort_by(|a, b| b.created_at.cmp(&a.created_at));
        active
            .iter()
            .skip(MAX_SESSIONS_PER_USER - 1)
            .map(|t| t.family_id)
            .collect()
    }

    #[test]
    fn eviction_keeps_the_nine_most_recent_families() {
        let now = Utc::now();
        let tokens: Vec<RefreshToken> = (0..11)
            .map(|i| {
                token(
                    false,
                    now - Duration::minutes(11 - i),
                    now + Duration::hours(1),
                )
            })
            .collect();

        let evicted = evicted_families(&tokens, now);
        // 11 familles actives : les 2 plus anciennes sont évincées.
        assert_eq!(evicted.len(), 2);
        assert!(evicted.contains(&tokens[0].family_id));
        assert!(evicted.contains(&tokens[1].family_id));
        assert!(!evicted.contains(&tokens[2].family_id));
        assert!(!evicted.contains(&tokens[10].family_id));
    }

    #[test]
    fn eviction_ignores_revoked_and_expired_tokens() {
        let now = Utc::now();
        let mut tokens: Vec<RefreshToken> = (0..9)
            .map(|i| token(false, now - Duration::minutes(i), now + Duration::hours(1)))
            .collect();
        let mut revoked = token(false, now - Duration::days(1), now + Duration::hours(1));
        revoked.revoked_at = Some(now);
        tokens.push(revoked);

        assert!(evicted_families(&tokens, now).is_empty());
    }

    /// Règle de purge : révoqué ou expiré depuis plus de 7 jours.
    fn is_stale(token: &RefreshToken, now: DateTime<Utc>) -> bool {
        let retention = Duration::days(REVOKED_TOKEN_RETENTION_DAYS);
        token
            .revoked_at
            .is_some_and(|revoked| revoked < now - retention)
            || token.expires_at < now - retention
    }

    #[test]
    fn stale_tokens_are_the_ones_past_retention() {
        let now = Utc::now();

        let fresh = token(false, now, now + Duration::hours(1));
        assert!(!is_stale(&fresh, now));

        let mut recently_revoked = token(false, now, now + Duration::hours(1));
        recently_revoked.revoked_at = Some(now - Duration::days(1));
        assert!(!is_stale(&recently_revoked, now));

        let mut long_revoked = token(false, now, now + Duration::hours(1));
        long_revoked.revoked_at = Some(now - Duration::days(8));
        assert!(is_stale(&long_revoked, now));

        let long_expired = token(false, now - Duration::days(30), now - Duration::days(8));
        assert!(is_stale(&long_expired, now));
    }

    /// Règle de la fenêtre de grâce à la rotation.
    fn within_grace(revoked_at: Option<DateTime<Utc>>, now: DateTime<Utc>) -> bool {
        revoked_at.is_some_and(|revoked| now - revoked <= Duration::seconds(ROTATION_GRACE_SECONDS))
    }

    #[test]
    fn the_grace_period_covers_thirty_seconds_after_rotation() {
        let now = Utc::now();
        assert!(within_grace(Some(now - Duration::seconds(5)), now));
        assert!(within_grace(Some(now - Duration::seconds(30)), now));
        assert!(!within_grace(Some(now - Duration::seconds(31)), now));
        assert!(!within_grace(None, now));
    }
}
