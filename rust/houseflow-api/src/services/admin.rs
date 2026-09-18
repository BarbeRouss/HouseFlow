//! Administration de la plateforme — portage de `AdminService`.
//!
//! L'amorçage des administrateurs (`PromoteBootstrapAdminsAsync`) vit dans
//! [`crate::services::bootstrap`], appelé au démarrage.

use chrono::{DateTime, Utc};
use serde_json::Value;
use sqlx::{FromRow, PgPool};
use uuid::Uuid;

use crate::audit::{self, AuditContext};
use crate::clock;
use crate::dto::admin::{AdminStatsDto, AdminUserDto, AdminUsersPageDto};
use crate::error::{AppError, AppResult};

/// `AdminService.MaxPageSize`.
pub const MAX_PAGE_SIZE: i64 = 100;

/// `GetStatsAsync` : compteurs bruts de la plateforme.
pub async fn get_stats(pool: &PgPool) -> AppResult<AdminStatsDto> {
    let (users, admins, houses, devices, maintenance_instances): (i64, i64, i64, i64, i64) =
        sqlx::query_as(
            r#"SELECT (SELECT COUNT(*) FROM "Users"),
                      (SELECT COUNT(*) FROM "Users" WHERE "IsAdmin"),
                      (SELECT COUNT(*) FROM "Houses"),
                      (SELECT COUNT(*) FROM "Devices"),
                      (SELECT COUNT(*) FROM "MaintenanceInstances")"#,
        )
        .fetch_one(pool)
        .await?;

    Ok(AdminStatsDto {
        users: users as i32,
        admins: admins as i32,
        houses: houses as i32,
        devices: devices as i32,
        maintenance_instances: maintenance_instances as i32,
    })
}

/// Normalise la pagination comme le C# : `page = Math.Max(page, 1)` et
/// `pageSize = Math.Clamp(pageSize, 1, MaxPageSize)`.
pub fn normalize_paging(page: i64, page_size: i64) -> (i64, i64) {
    (page.max(1), page_size.clamp(1, MAX_PAGE_SIZE))
}

/// Ligne de la page d'utilisateurs, avec le nombre de maisons possédées
/// (`u.Houses.Count`).
#[derive(Debug, Clone, FromRow)]
#[sqlx(rename_all = "PascalCase")]
struct AdminUserRow {
    id: Uuid,
    email: String,
    first_name: String,
    last_name: String,
    is_admin: bool,
    created_at: DateTime<Utc>,
    houses_count: i64,
}

impl From<AdminUserRow> for AdminUserDto {
    fn from(row: AdminUserRow) -> Self {
        Self {
            id: row.id,
            email: row.email,
            first_name: row.first_name,
            last_name: row.last_name,
            is_admin: row.is_admin,
            created_at: row.created_at,
            houses_count: row.houses_count as i32,
        }
    }
}

/// `GetUsersAsync` : recherche insensible à la casse sur l'e-mail, le prénom et le
/// nom, triée par e-mail.
pub async fn get_users(
    pool: &PgPool,
    search: Option<&str>,
    page: i64,
    page_size: i64,
) -> AppResult<AdminUsersPageDto> {
    let (page, page_size) = normalize_paging(page, page_size);

    // `string.IsNullOrWhiteSpace(search)` ⇒ aucun filtre.
    let term = search
        .map(str::trim)
        .filter(|value| !value.is_empty())
        .map(str::to_lowercase);

    // `POSITION(term IN LOWER(col)) > 0`, la traduction Npgsql de `string.Contains`,
    // qui évite d'avoir à échapper `%` et `_`.
    let filter = r#"($1::text IS NULL
                     OR POSITION($1 IN LOWER("Email")) > 0
                     OR POSITION($1 IN LOWER("FirstName")) > 0
                     OR POSITION($1 IN LOWER("LastName")) > 0)"#;

    let (total,): (i64,) =
        sqlx::query_as(&format!(r#"SELECT COUNT(*) FROM "Users" WHERE {filter}"#))
            .bind(term.as_deref())
            .fetch_one(pool)
            .await?;

    let rows: Vec<AdminUserRow> = sqlx::query_as(&format!(
        r#"SELECT u."Id", u."Email", u."FirstName", u."LastName", u."IsAdmin", u."CreatedAt",
                  (SELECT COUNT(*) FROM "Houses" h WHERE h."UserId" = u."Id") AS "HousesCount"
             FROM "Users" u
            WHERE {filter}
            ORDER BY u."Email"
            OFFSET $2 LIMIT $3"#
    ))
    .bind(term.as_deref())
    .bind((page - 1) * page_size)
    .bind(page_size)
    .fetch_all(pool)
    .await?;

    Ok(AdminUsersPageDto {
        users: rows.into_iter().map(AdminUserDto::from).collect(),
        total: total as i32,
        page: page as i32,
        page_size: page_size as i32,
    })
}

/// Utilisateur visé par `SetAdminAsync`.
#[derive(Debug, Clone, FromRow)]
#[sqlx(rename_all = "PascalCase")]
struct TargetUser {
    email: String,
    first_name: String,
    last_name: String,
    is_admin: bool,
    created_at: DateTime<Utc>,
}

/// `SetAdminAsync` : accorde ou retire les droits d'administration. Un administrateur
/// ne peut pas se retirer les siens.
pub async fn set_admin(
    pool: &PgPool,
    context: &AuditContext,
    acting_user_id: Uuid,
    target_user_id: Uuid,
    is_admin: bool,
) -> AppResult<AdminUserDto> {
    if !is_admin && acting_user_id == target_user_id {
        return Err(AppError::BadRequest(
            "You cannot remove your own administrator rights.".to_string(),
        ));
    }

    let mut tx = pool.begin().await?;

    let user: Option<TargetUser> = sqlx::query_as(
        r#"SELECT "Email", "FirstName", "LastName", "IsAdmin", "CreatedAt"
             FROM "Users" WHERE "Id" = $1 FOR UPDATE"#,
    )
    .bind(target_user_id)
    .fetch_optional(&mut *tx)
    .await?;

    let Some(user) = user else {
        return Err(AppError::NotFound("User not found".to_string()));
    };
    let was_admin = user.is_admin;

    // Le C# n'appelle `SaveChangesAsync` que si la valeur change : sans changement,
    // aucune ligne d'audit et aucune mise à jour de `UpdatedAt`.
    if was_admin != is_admin {
        let now = clock::now();
        sqlx::query(r#"UPDATE "Users" SET "IsAdmin" = $1, "UpdatedAt" = $2 WHERE "Id" = $3"#)
            .bind(is_admin)
            .bind(now)
            .bind(target_user_id)
            .execute(&mut *tx)
            .await?;

        audit::record_modified(
            &mut tx,
            context,
            "User",
            target_user_id,
            &audit::values([
                ("IsAdmin", Value::Bool(was_admin)),
                ("UpdatedAt", Value::Null),
            ]),
            &audit::values([
                ("IsAdmin", Value::Bool(is_admin)),
                ("UpdatedAt", audit::date(now)),
            ]),
            &["IsAdmin", "UpdatedAt"],
        )
        .await?;

        tracing::info!(
            user_id = %target_user_id,
            acting_user_id = %acting_user_id,
            action = if is_admin { "granted" } else { "revoked" },
            "admin rights updated"
        );
    }

    let (houses_count,): (i64,) =
        sqlx::query_as(r#"SELECT COUNT(*) FROM "Houses" WHERE "UserId" = $1"#)
            .bind(target_user_id)
            .fetch_one(&mut *tx)
            .await?;

    tx.commit().await?;

    Ok(AdminUserDto {
        id: target_user_id,
        email: user.email,
        first_name: user.first_name,
        last_name: user.last_name,
        is_admin,
        created_at: user.created_at,
        houses_count: houses_count as i32,
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn paging_is_clamped_like_the_csharp_service() {
        assert_eq!(normalize_paging(1, 20), (1, 20));
        assert_eq!(normalize_paging(0, 20), (1, 20));
        assert_eq!(normalize_paging(-5, 20), (1, 20));
        assert_eq!(normalize_paging(3, 1000), (3, MAX_PAGE_SIZE));
        assert_eq!(normalize_paging(3, 0), (3, 1));
        assert_eq!(normalize_paging(3, -1), (3, 1));
    }
}
