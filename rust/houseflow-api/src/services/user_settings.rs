//! Préférences utilisateur — portage de `UserSettingsService`.

use chrono::Utc;
use serde_json::Value;
use sqlx::PgPool;
use uuid::Uuid;

use crate::audit::{self, AuditContext};
use crate::clock;
use crate::dto::user_settings::{UpdateUserSettings, UserSettings};
use crate::error::{AppError, AppResult};

/// Lit le thème et la langue de l'utilisateur.
pub async fn get_settings(pool: &PgPool, user_id: Uuid) -> AppResult<UserSettings> {
    let row: Option<(String, String)> =
        sqlx::query_as(r#"SELECT "Theme", "Language" FROM "Users" WHERE "Id" = $1"#)
            .bind(user_id)
            .fetch_optional(pool)
            .await?;

    let (theme, language) = row.ok_or_else(user_not_found)?;
    Ok(UserSettings { theme, language })
}

/// Met à jour le thème et la langue, et touche `UpdatedAt`.
pub async fn update_settings(
    pool: &PgPool,
    context: &AuditContext,
    user_id: Uuid,
    settings: &UpdateUserSettings,
) -> AppResult<UserSettings> {
    let mut tx = pool.begin().await?;

    let current: Option<(String, String, Option<chrono::DateTime<Utc>>)> = sqlx::query_as(
        r#"SELECT "Theme", "Language", "UpdatedAt" FROM "Users" WHERE "Id" = $1 FOR UPDATE"#,
    )
    .bind(user_id)
    .fetch_optional(&mut *tx)
    .await?;

    let (old_theme, old_language, old_updated_at) = current.ok_or_else(user_not_found)?;

    let now = clock::now();
    let theme = settings.theme().to_string();
    let language = settings.language().to_string();

    sqlx::query(
        r#"UPDATE "Users" SET "Theme" = $1, "Language" = $2, "UpdatedAt" = $3 WHERE "Id" = $4"#,
    )
    .bind(&theme)
    .bind(&language)
    .bind(now)
    .bind(user_id)
    .execute(&mut *tx)
    .await?;

    // EF ne journalise que les propriétés réellement modifiées.
    let mut changed = vec!["UpdatedAt"];
    let mut old = audit::values([("UpdatedAt", audit::opt_date(old_updated_at))]);
    let mut new = audit::values([("UpdatedAt", audit::date(now))]);
    if old_theme != theme {
        changed.push("Theme");
        old.insert("Theme".into(), Value::String(old_theme));
        new.insert("Theme".into(), Value::String(theme.clone()));
    }
    if old_language != language {
        changed.push("Language");
        old.insert("Language".into(), Value::String(old_language));
        new.insert("Language".into(), Value::String(language.clone()));
    }

    audit::record_modified(&mut tx, context, "User", user_id, &old, &new, &changed).await?;
    tx.commit().await?;

    Ok(UserSettings { theme, language })
}

fn user_not_found() -> AppError {
    // KeyNotFoundException("User not found") ⇒ 404 { "error": … }.
    AppError::NotFound("User not found".to_string())
}
