//! Point d'entrée : configuration, base, migrations, amorçage, routeur, tâches, écoute.

use std::net::SocketAddr;

use houseflow_api::config::Config;
use houseflow_api::services::bootstrap;
use houseflow_api::state::AppState;
use houseflow_api::{db, jobs, routes};
use tokio::signal;
use tracing_subscriber::EnvFilter;

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    init_tracing();

    let migrate_only = std::env::args().any(|arg| arg == "--migrate");
    let config = Config::from_env()?;

    // Mode `--migrate` : appliquer les migrations et sortir (conteneurs d'init).
    if migrate_only {
        db::ensure_database_exists(&config.database_url).await?;
        let pool = db::create_pool(&config.database_url).await?;
        db::run_migrations(&pool).await?;
        pool.close().await;
        return Ok(());
    }

    if config.auto_migrate {
        db::ensure_database_exists(&config.database_url).await?;
    }

    let pool = db::create_pool(&config.database_url).await?;

    if config.auto_migrate {
        db::run_migrations(&pool).await?;
    }

    // Amorçage des administrateurs (tous environnements).
    let promoted = bootstrap::promote_bootstrap_admins(&pool, &config).await?;
    if promoted > 0 {
        tracing::info!(count = promoted, "bootstrap administrator(s) promoted");
    }

    // Comptes de développement / démonstration.
    if config.is_development() {
        bootstrap::seed_development_admin(&pool).await?;
    }
    if config.demo_mode {
        bootstrap::seed_demo_user(&pool).await?;
    }

    // Tâches de fond : première exécution immédiate, puis toutes les 24 heures.
    jobs::run_all(&pool).await;
    let jobs_handle = jobs::spawn_periodic(pool.clone());

    let port = config.port;
    let state = AppState::new(pool.clone(), config);
    let app = routes::build(state);

    let address = SocketAddr::from(([0, 0, 0, 0], port));
    let listener = tokio::net::TcpListener::bind(address).await?;
    tracing::info!(%address, "HouseFlow API listening");

    axum::serve(
        listener,
        app.into_make_service_with_connect_info::<SocketAddr>(),
    )
    .with_graceful_shutdown(shutdown_signal())
    .await?;

    jobs_handle.abort();
    pool.close().await;
    tracing::info!("HouseFlow API stopped");

    Ok(())
}

fn init_tracing() {
    let filter = EnvFilter::try_from_default_env().unwrap_or_else(|_| EnvFilter::new("info"));
    tracing_subscriber::fmt()
        .compact()
        .with_env_filter(filter)
        .init();
}

/// Arrêt propre sur SIGINT (Ctrl+C) et SIGTERM (arrêt de conteneur).
async fn shutdown_signal() {
    let ctrl_c = async {
        let _ = signal::ctrl_c().await;
    };

    #[cfg(unix)]
    let terminate = async {
        match signal::unix::signal(signal::unix::SignalKind::terminate()) {
            Ok(mut stream) => {
                stream.recv().await;
            }
            Err(error) => tracing::error!(error = %error, "failed to install SIGTERM handler"),
        }
    };

    #[cfg(not(unix))]
    let terminate = std::future::pending::<()>();

    tokio::select! {
        _ = ctrl_c => tracing::info!("received SIGINT, shutting down"),
        _ = terminate => tracing::info!("received SIGTERM, shutting down"),
    }
}
