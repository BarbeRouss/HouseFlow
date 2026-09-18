//! Tâches de fond.
//!
//! Hangfire est remplacé par une simple tâche `tokio` périodique : première
//! exécution au démarrage, puis toutes les 24 heures (`Cron.Daily` côté .NET).
//!
//! Phase 2/3 : ajouter ici les tâches supplémentaires en les appelant depuis
//! [`run_all`], pour qu'elles partagent le même rythme et la même gestion d'erreur.

pub mod cleanup_expired_invitations;

use std::time::Duration;

use sqlx::PgPool;
use tokio::task::JoinHandle;

/// Intervalle entre deux passages (24 h).
pub const INTERVAL: Duration = Duration::from_secs(24 * 60 * 60);

/// Exécute une fois toutes les tâches périodiques.
pub async fn run_all(pool: &PgPool) {
    if let Err(error) = cleanup_expired_invitations::execute(pool).await {
        tracing::error!(error = %error, "cleanup-expired-invitations job failed");
    }
}

/// Démarre la boucle périodique en tâche de fond.
pub fn spawn_periodic(pool: PgPool) -> JoinHandle<()> {
    tokio::spawn(async move {
        let mut ticker = tokio::time::interval(INTERVAL);
        loop {
            ticker.tick().await;
            run_all(&pool).await;
        }
    })
}
