//! Accès à la base : pool, création de la base si absente, migrations.

use sqlx::migrate::Migrator;
use sqlx::postgres::{PgConnectOptions, PgPoolOptions};
use sqlx::{ConnectOptions, Connection, PgConnection, PgPool};
use std::str::FromStr;

/// Migrations embarquées dans le binaire (`rust/houseflow-api/migrations`).
pub static MIGRATOR: Migrator = sqlx::migrate!("./migrations");

/// Ouvre le pool de connexions.
pub async fn create_pool(database_url: &str) -> anyhow::Result<PgPool> {
    let pool = PgPoolOptions::new()
        .max_connections(20)
        .connect(database_url)
        .await?;
    Ok(pool)
}

/// Crée la base si elle n'existe pas, en se connectant à la base de maintenance
/// `postgres` — équivalent de ce que fait `Database.Migrate()` côté EF.
pub async fn ensure_database_exists(database_url: &str) -> anyhow::Result<()> {
    let options = PgConnectOptions::from_str(database_url)?;
    let database_name = options
        .get_database()
        .ok_or_else(|| anyhow::anyhow!("DATABASE_URL must name a database"))?
        .to_string();

    let maintenance = options
        .clone()
        .database("postgres")
        .disable_statement_logging();
    let mut connection = PgConnection::connect_with(&maintenance).await?;

    let exists: Option<(i32,)> = sqlx::query_as("SELECT 1 FROM pg_database WHERE datname = $1")
        .bind(&database_name)
        .fetch_optional(&mut connection)
        .await?;

    if exists.is_none() {
        tracing::info!(database = %database_name, "creating database");
        // Le nom de base ne peut pas être un paramètre lié : il est cité explicitement.
        sqlx::query(&format!(
            r#"CREATE DATABASE "{}""#,
            database_name.replace('"', "\"\"")
        ))
        .execute(&mut connection)
        .await?;
    }

    connection.close().await?;
    Ok(())
}

/// Applique les migrations en attente.
pub async fn run_migrations(pool: &PgPool) -> anyhow::Result<()> {
    tracing::info!("running database migrations...");
    MIGRATOR.run(pool).await?;
    tracing::info!("database migrations applied successfully.");
    Ok(())
}

/// Sonde de santé : `SELECT 1`.
pub async fn ping(pool: &PgPool) -> bool {
    sqlx::query("SELECT 1").execute(pool).await.is_ok()
}
