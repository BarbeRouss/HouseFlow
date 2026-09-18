//! Nettoyage des invitations — portage de `CleanupExpiredInvitationsJob` (Hangfire).

use chrono::Duration;
use sqlx::PgPool;

use crate::clock;
use crate::models::InvitationStatus;

/// Les invitations non actives plus anciennes que ce délai sont supprimées.
const DELETE_AFTER_DAYS: i64 = 30;

pub async fn execute(pool: &PgPool) -> anyhow::Result<()> {
    let now = clock::now();

    // 1. Marquer expirées les invitations en attente dont la date est passée.
    let expired = sqlx::query(
        r#"UPDATE "Invitations" SET "Status" = $1 WHERE "Status" = $2 AND "ExpiresAt" <= $3"#,
    )
    .bind(InvitationStatus::Expired.to_string())
    .bind(InvitationStatus::Pending.to_string())
    .bind(now)
    .execute(pool)
    .await?
    .rows_affected();

    if expired > 0 {
        tracing::info!(count = expired, "marked expired invitations");
    }

    // 2. Supprimer les invitations non en attente de plus de 30 jours.
    let cutoff = now - Duration::days(DELETE_AFTER_DAYS);
    let deleted =
        sqlx::query(r#"DELETE FROM "Invitations" WHERE "Status" <> $1 AND "ExpiresAt" <= $2"#)
            .bind(InvitationStatus::Pending.to_string())
            .bind(cutoff)
            .execute(pool)
            .await?
            .rows_affected();

    if deleted > 0 {
        tracing::info!(
            count = deleted,
            days = DELETE_AFTER_DAYS,
            "deleted old invitations"
        );
    }

    Ok(())
}
