//! Amorçage au démarrage : promotion des administrateurs et jeux de données de dev.
//!
//! Portage de `AdminService.PromoteBootstrapAdminsAsync` et des blocs de seed de
//! `Program.cs`.

use serde_json::Value;
use sqlx::PgPool;
use uuid::Uuid;

use crate::audit::{self, AuditContext};
use crate::auth::password;
use crate::clock;
use crate::config::{Config, DEMO_EMAIL};
use crate::error::AppResult;
use crate::models::HouseRole;

/// Promeut les comptes déjà existants listés dans `ADMIN__BOOTSTRAP_EMAILS`.
///
/// Les autres reçoivent le drapeau à l'inscription (cf. `Config::is_bootstrap_admin`).
pub async fn promote_bootstrap_admins(pool: &PgPool, config: &Config) -> AppResult<u64> {
    if config.bootstrap_emails.is_empty() {
        return Ok(0);
    }

    let lowered: Vec<String> = config
        .bootstrap_emails
        .iter()
        .map(|email| email.to_lowercase())
        .collect();

    let candidates: Vec<(Uuid, String)> = sqlx::query_as(
        r#"SELECT "Id", "Email" FROM "Users" WHERE NOT "IsAdmin" AND lower("Email") = ANY($1)"#,
    )
    .bind(&lowered)
    .fetch_all(pool)
    .await?;

    if candidates.is_empty() {
        return Ok(0);
    }

    let now = clock::now();
    let mut tx = pool.begin().await?;
    for (id, email) in &candidates {
        sqlx::query(r#"UPDATE "Users" SET "IsAdmin" = true, "UpdatedAt" = $1 WHERE "Id" = $2"#)
            .bind(now)
            .bind(id)
            .execute(&mut *tx)
            .await?;

        audit::record_modified(
            &mut tx,
            &AuditContext::anonymous(),
            "User",
            *id,
            &audit::values([("IsAdmin", Value::Bool(false)), ("UpdatedAt", Value::Null)]),
            &audit::values([
                ("IsAdmin", Value::Bool(true)),
                ("UpdatedAt", audit::date(now)),
            ]),
            &["IsAdmin", "UpdatedAt"],
        )
        .await?;

        tracing::info!(email = %email, "bootstrap admin promoted");
    }
    tx.commit().await?;

    Ok(candidates.len() as u64)
}

/// Compte `admin@admin.com` / `admin` en développement (jamais en production).
pub async fn seed_development_admin(pool: &PgPool) -> AppResult<()> {
    const ADMIN_EMAIL: &str = "admin@admin.com";

    if user_exists(pool, ADMIN_EMAIL).await? {
        return Ok(());
    }

    let context = AuditContext::anonymous();
    let mut tx = pool.begin().await?;
    let user_id = insert_user(
        &mut tx,
        &context,
        ADMIN_EMAIL,
        "admin",
        "Admin",
        "User",
        false,
    )
    .await?;
    tx.commit().await?;

    tracing::info!(email = %ADMIN_EMAIL, user_id = %user_id, "default admin user created");
    Ok(())
}

/// Compte de démonstration (`DEMO_MODE=true`) : administrateur, avec sa maison.
pub async fn seed_demo_user(pool: &PgPool) -> AppResult<()> {
    if user_exists(pool, DEMO_EMAIL).await? {
        return Ok(());
    }

    let context = AuditContext::anonymous();
    let now = clock::now();
    let mut tx = pool.begin().await?;

    let user_id = insert_user(
        &mut tx,
        &context,
        DEMO_EMAIL,
        "Demo@2026!",
        "Demo",
        "User",
        true,
    )
    .await?;

    let house_id = Uuid::new_v4();
    sqlx::query(
        r#"INSERT INTO "Houses" ("Id", "Name", "CreatedAt", "UserId") VALUES ($1, $2, $3, $4)"#,
    )
    .bind(house_id)
    .bind("Ma maison")
    .bind(now)
    .bind(user_id)
    .execute(&mut *tx)
    .await?;
    audit::record_added(
        &mut tx,
        &context,
        "House",
        house_id,
        &audit::values([
            ("Id", audit::id(house_id)),
            ("Name", Value::String("Ma maison".into())),
            ("CreatedAt", audit::date(now)),
            ("UserId", audit::id(user_id)),
        ]),
    )
    .await?;

    let member_id = Uuid::new_v4();
    sqlx::query(
        r#"INSERT INTO "HouseMembers"
               ("Id", "Role", "CanLogMaintenance", "CreatedAt", "UserId", "HouseId", "CanViewCosts")
           VALUES ($1, $2, true, $3, $4, $5, false)"#,
    )
    .bind(member_id)
    .bind(HouseRole::Owner.to_string())
    .bind(now)
    .bind(user_id)
    .bind(house_id)
    .execute(&mut *tx)
    .await?;
    audit::record_added(
        &mut tx,
        &context,
        "HouseMember",
        member_id,
        &audit::values([
            ("Id", audit::id(member_id)),
            ("Role", Value::String(HouseRole::Owner.to_string())),
            ("CanLogMaintenance", Value::Bool(true)),
            ("CanViewCosts", Value::Bool(false)),
            ("CreatedAt", audit::date(now)),
            ("UserId", audit::id(user_id)),
            ("HouseId", audit::id(house_id)),
        ]),
    )
    .await?;

    tx.commit().await?;

    tracing::info!(email = %DEMO_EMAIL, "demo user created (password: Demo@2026!)");
    Ok(())
}

async fn user_exists(pool: &PgPool, email: &str) -> AppResult<bool> {
    let row: Option<(i32,)> = sqlx::query_as(r#"SELECT 1 FROM "Users" WHERE "Email" = $1"#)
        .bind(email)
        .fetch_optional(pool)
        .await?;
    Ok(row.is_some())
}

#[allow(clippy::too_many_arguments)]
async fn insert_user(
    tx: &mut sqlx::Transaction<'_, sqlx::Postgres>,
    context: &AuditContext,
    email: &str,
    raw_password: &str,
    first_name: &str,
    last_name: &str,
    is_admin: bool,
) -> AppResult<Uuid> {
    let id = Uuid::new_v4();
    let now = clock::now();
    let password_hash = password::hash(raw_password);

    sqlx::query(
        r#"INSERT INTO "Users"
               ("Id", "Email", "PasswordHash", "CreatedAt", "FirstName", "LastName", "IsAdmin")
           VALUES ($1, $2, $3, $4, $5, $6, $7)"#,
    )
    .bind(id)
    .bind(email)
    .bind(&password_hash)
    .bind(now)
    .bind(first_name)
    .bind(last_name)
    .bind(is_admin)
    .execute(&mut **tx)
    .await?;

    audit::record_added(
        tx,
        context,
        "User",
        id,
        &audit::values([
            ("Id", audit::id(id)),
            ("Email", Value::String(email.to_string())),
            ("PasswordHash", Value::String(password_hash)),
            ("CreatedAt", audit::date(now)),
            ("FirstName", Value::String(first_name.to_string())),
            ("LastName", Value::String(last_name.to_string())),
            ("IsAdmin", Value::Bool(is_admin)),
        ]),
    )
    .await?;

    Ok(id)
}
