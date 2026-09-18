//! Tests du service d'authentification **avec** une vraie base PostgreSQL.
//!
//! Ils sont `#[ignore]` : `cargo test` seul reste hors-ligne. Pour les jouer :
//!
//! ```bash
//! DATABASE_URL=postgres://postgres:postgres@localhost:5432/houseflow_rust_tests \
//!   cargo test -p houseflow-api --test auth_db -- --ignored --test-threads=1
//! ```
//!
//! La base est créée et migrée au besoin ; chaque test utilise un e-mail unique et
//! ne dépend donc pas d'un état initial.

use chrono::{Duration, Utc};
use houseflow_api::audit::AuditContext;
use houseflow_api::config::{Config, SameSite};
use houseflow_api::db;
use houseflow_api::dto::auth::{LoginRequest, RegisterRequest};
use houseflow_api::services::auth;
use sqlx::PgPool;
use uuid::Uuid;

const PASSWORD: &str = "Password123!";

fn config() -> Config {
    Config {
        database_url: database_url(),
        port: 0,
        app_env: "Development".into(),
        auto_migrate: true,
        jwt_key: "DevOnlySecretKey_DO_NOT_USE_IN_PRODUCTION_MinimumLengthRequired256Bits!".into(),
        jwt_issuer: "HouseFlowAPI".into(),
        jwt_audience: "HouseFlowClient".into(),
        bootstrap_emails: vec!["julienrousselle@outlook.be".into()],
        demo_mode: false,
        cors_origins: None,
        cookie_same_site: SameSite::Lax,
    }
}

fn database_url() -> String {
    std::env::var("DATABASE_URL")
        .expect("DATABASE_URL must be set to run the database-backed tests")
}

async fn pool() -> PgPool {
    let url = database_url();
    db::ensure_database_exists(&url)
        .await
        .expect("database should be reachable");
    let pool = db::create_pool(&url).await.expect("pool should open");
    db::run_migrations(&pool)
        .await
        .expect("migrations should apply");
    pool
}

fn unique_email(prefix: &str) -> String {
    format!("{prefix}-{}@example.com", Uuid::new_v4())
}

fn register_request(email: &str) -> RegisterRequest {
    serde_json::from_value(serde_json::json!({
        "email": email,
        "firstName": "Test",
        "lastName": "User",
        "password": PASSWORD,
    }))
    .unwrap()
}

fn login_request(email: &str, remember_me: bool) -> LoginRequest {
    serde_json::from_value(serde_json::json!({
        "email": email,
        "password": PASSWORD,
        "rememberMe": remember_me,
    }))
    .unwrap()
}

#[tokio::test]
#[ignore = "requires PostgreSQL"]
async fn register_creates_the_user_its_default_house_and_an_owner_membership() {
    let pool = pool().await;
    let config = config();
    let email = unique_email("register");

    let outcome = auth::register(&pool, &config, &register_request(&email), None, None)
        .await
        .expect("registration should succeed");

    assert!(!outcome.response.access_token.is_empty());
    assert_eq!(outcome.response.user.email, email);
    assert_eq!(outcome.response.expires_in, 900);
    // Une inscription n'est jamais une session « se souvenir de moi ».
    assert!(outcome.cookie_expires_at.is_none());

    let user_id = outcome.response.user.id;
    let (house_name,): (String,) =
        sqlx::query_as(r#"SELECT "Name" FROM "Houses" WHERE "UserId" = $1"#)
            .bind(user_id)
            .fetch_one(&pool)
            .await
            .unwrap();
    assert_eq!(house_name, "Ma maison");

    let (role, can_log): (String, bool) = sqlx::query_as(
        r#"SELECT "Role", "CanLogMaintenance" FROM "HouseMembers" WHERE "UserId" = $1"#,
    )
    .bind(user_id)
    .fetch_one(&pool)
    .await
    .unwrap();
    assert_eq!(role, "Owner");
    assert!(can_log);
}

#[tokio::test]
#[ignore = "requires PostgreSQL"]
async fn register_with_an_existing_email_is_a_conflict() {
    let pool = pool().await;
    let config = config();
    let email = unique_email("duplicate");

    auth::register(&pool, &config, &register_request(&email), None, None)
        .await
        .unwrap();
    let error = auth::register(&pool, &config, &register_request(&email), None, None)
        .await
        .unwrap_err();

    assert!(
        matches!(error, houseflow_api::error::AppError::Conflict(ref message) if message.contains("already registered")),
        "expected a conflict, got {error:?}"
    );
}

#[tokio::test]
#[ignore = "requires PostgreSQL"]
async fn login_rejects_a_wrong_password_and_an_unknown_account() {
    let pool = pool().await;
    let config = config();
    let email = unique_email("login");
    auth::register(&pool, &config, &register_request(&email), None, None)
        .await
        .unwrap();

    let mut wrong = login_request(&email, false);
    wrong = serde_json::from_value(serde_json::json!({
        "email": wrong.email(),
        "password": "WrongPassword!",
    }))
    .unwrap();

    for request in [wrong, login_request(&unique_email("ghost"), false)] {
        let error = auth::login(&pool, &config, &request, None)
            .await
            .unwrap_err();
        assert!(
            matches!(error, houseflow_api::error::AppError::Unauthorized(ref m) if m == "Invalid email or password"),
            "expected a 401, got {error:?}"
        );
    }
}

#[tokio::test]
#[ignore = "requires PostgreSQL"]
async fn remember_me_drives_the_refresh_token_lifetime() {
    let pool = pool().await;
    let config = config();
    let email = unique_email("remember");
    auth::register(&pool, &config, &register_request(&email), None, None)
        .await
        .unwrap();

    let persistent = auth::login(&pool, &config, &login_request(&email, true), None)
        .await
        .unwrap();
    let session = auth::login(&pool, &config, &login_request(&email, false), None)
        .await
        .unwrap();

    let expiry = persistent.cookie_expires_at.expect("persistent cookie");
    assert!(
        (expiry - (Utc::now() + Duration::days(365)))
            .num_minutes()
            .abs()
            < 5
    );
    assert!(session.cookie_expires_at.is_none());
}

#[tokio::test]
#[ignore = "requires PostgreSQL"]
async fn refresh_rotates_the_token_and_keeps_its_family_and_lifetime() {
    let pool = pool().await;
    let config = config();
    let email = unique_email("rotate");
    auth::register(&pool, &config, &register_request(&email), None, None)
        .await
        .unwrap();

    let first = auth::login(&pool, &config, &login_request(&email, true), None)
        .await
        .unwrap();
    let second = auth::refresh(&pool, &config, &first.refresh_token, None)
        .await
        .unwrap();

    assert_ne!(first.refresh_token, second.refresh_token);
    assert!(
        second.cookie_expires_at.is_some(),
        "remember me is preserved"
    );

    let (family_a,): (Uuid,) =
        sqlx::query_as(r#"SELECT "FamilyId" FROM "RefreshTokens" WHERE "Token" = $1"#)
            .bind(&first.refresh_token)
            .fetch_one(&pool)
            .await
            .unwrap();
    let (family_b, remember_me): (Uuid, bool) = sqlx::query_as(
        r#"SELECT "FamilyId", "RememberMe" FROM "RefreshTokens" WHERE "Token" = $1"#,
    )
    .bind(&second.refresh_token)
    .fetch_one(&pool)
    .await
    .unwrap();
    assert_eq!(family_a, family_b, "rotation stays in the same family");
    assert!(remember_me);

    let (reason, replaced_by): (Option<String>, Option<String>) = sqlx::query_as(
        r#"SELECT "ReasonRevoked", "ReplacedByToken" FROM "RefreshTokens" WHERE "Token" = $1"#,
    )
    .bind(&first.refresh_token)
    .fetch_one(&pool)
    .await
    .unwrap();
    assert_eq!(reason.as_deref(), Some("Replaced by new token"));
    assert_eq!(replaced_by.as_deref(), Some(second.refresh_token.as_str()));
}

#[tokio::test]
#[ignore = "requires PostgreSQL"]
async fn a_rotated_token_replayed_within_the_grace_period_returns_the_current_one() {
    let pool = pool().await;
    let config = config();
    let email = unique_email("grace");
    auth::register(&pool, &config, &register_request(&email), None, None)
        .await
        .unwrap();

    let a1 = auth::login(&pool, &config, &login_request(&email, true), None)
        .await
        .unwrap();
    let a2 = auth::refresh(&pool, &config, &a1.refresh_token, None)
        .await
        .unwrap();

    // Deux onglets qui démarrent ensemble envoient le même cookie : le perdant de la
    // course ne doit pas être traité comme un voleur.
    let replay = auth::refresh(&pool, &config, &a1.refresh_token, None)
        .await
        .expect("the replay should be tolerated");
    assert_eq!(replay.refresh_token, a2.refresh_token);

    // Le jeton courant reste utilisable.
    auth::refresh(&pool, &config, &a2.refresh_token, None)
        .await
        .expect("the current token still works");
}

#[tokio::test]
#[ignore = "requires PostgreSQL"]
async fn a_rotated_token_replayed_outside_the_grace_period_revokes_its_family_only() {
    let pool = pool().await;
    let config = config();
    let email = unique_email("reuse");
    auth::register(&pool, &config, &register_request(&email), None, None)
        .await
        .unwrap();

    let device_a1 = auth::login(&pool, &config, &login_request(&email, true), None)
        .await
        .unwrap();
    let device_b1 = auth::login(&pool, &config, &login_request(&email, true), None)
        .await
        .unwrap();
    let device_a2 = auth::refresh(&pool, &config, &device_a1.refresh_token, None)
        .await
        .unwrap();
    let device_a3 = auth::refresh(&pool, &config, &device_a2.refresh_token, None)
        .await
        .unwrap();

    // a1 a deux rotations de retard : son remplaçant n'est plus actif, ce n'est donc
    // pas une course.
    assert!(
        auth::refresh(&pool, &config, &device_a1.refresh_token, None)
            .await
            .is_err()
    );
    assert!(
        auth::refresh(&pool, &config, &device_a3.refresh_token, None)
            .await
            .is_err(),
        "the whole family is revoked"
    );
    auth::refresh(&pool, &config, &device_b1.refresh_token, None)
        .await
        .expect("other devices are untouched");
}

#[tokio::test]
#[ignore = "requires PostgreSQL"]
async fn opening_more_than_ten_sessions_evicts_the_least_recently_used() {
    let pool = pool().await;
    let config = config();
    let email = unique_email("sessions");
    auth::register(&pool, &config, &register_request(&email), None, None)
        .await
        .unwrap();

    let mut sessions = Vec::new();
    for _ in 0..10 {
        sessions.push(
            auth::login(&pool, &config, &login_request(&email, false), None)
                .await
                .unwrap()
                .refresh_token,
        );
    }

    // L'inscription a ouvert une session aussi : celle-ci est la douzième, elle évince
    // celle de l'inscription et celle de la première connexion.
    sessions.push(
        auth::login(&pool, &config, &login_request(&email, false), None)
            .await
            .unwrap()
            .refresh_token,
    );

    assert!(auth::refresh(&pool, &config, &sessions[0], None)
        .await
        .is_err());
    auth::refresh(&pool, &config, &sessions[1], None)
        .await
        .expect("the second session survives");
    auth::refresh(&pool, &config, &sessions[10], None)
        .await
        .expect("the newest session works");
}

#[tokio::test]
#[ignore = "requires PostgreSQL"]
async fn revoking_a_token_makes_it_unusable_and_revoking_twice_fails() {
    let pool = pool().await;
    let config = config();
    let email = unique_email("revoke");
    let outcome = auth::register(&pool, &config, &register_request(&email), None, None)
        .await
        .unwrap();

    let context = AuditContext::overridden(
        Some(outcome.response.user.id),
        Some(email.clone()),
        Some("127.0.0.1".into()),
    );

    auth::revoke_token(&pool, &context, &outcome.refresh_token, None)
        .await
        .expect("the first revocation succeeds");

    assert!(auth::refresh(&pool, &config, &outcome.refresh_token, None)
        .await
        .is_err());

    let error = auth::revoke_token(&pool, &context, &outcome.refresh_token, None)
        .await
        .unwrap_err();
    assert!(
        matches!(error, houseflow_api::error::AppError::BadRequest(ref m) if m == "Invalid or expired token"),
        "expected a 400, got {error:?}"
    );
}

#[tokio::test]
#[ignore = "requires PostgreSQL"]
async fn a_bootstrap_email_is_flagged_admin_at_registration() {
    let pool = pool().await;
    let mut config = config();
    let email = unique_email("bootstrap");
    config.bootstrap_emails = vec![email.to_uppercase()];

    let outcome = auth::register(&pool, &config, &register_request(&email), None, None)
        .await
        .unwrap();

    assert!(
        outcome.response.user.is_admin,
        "the bootstrap list is matched case-insensitively"
    );
}

#[tokio::test]
#[ignore = "requires PostgreSQL"]
async fn every_mutation_leaves_an_audit_trail() {
    let pool = pool().await;
    let config = config();
    let email = unique_email("audit");

    let outcome = auth::register(&pool, &config, &register_request(&email), None, Some(""))
        .await
        .unwrap();
    let user_id = outcome.response.user.id;

    let rows: Vec<(String, String, Option<String>)> = sqlx::query_as(
        r#"SELECT "EntityType", "Action", "Username" FROM "AuditLogs"
            WHERE "Username" = $1 ORDER BY "Timestamp""#,
    )
    .bind(&email)
    .fetch_all(&pool)
    .await
    .unwrap();

    let entities: Vec<&str> = rows.iter().map(|(entity, _, _)| entity.as_str()).collect();
    for expected in ["User", "House", "HouseMember", "RefreshToken"] {
        assert!(
            entities.contains(&expected),
            "{expected} should be audited, got {entities:?}"
        );
    }
    assert!(rows.iter().all(|(_, action, _)| action == "Added"));

    // L'inscription n'a pas de JWT : l'identité est nulle, l'e-mail sert de nom.
    let (null_user_rows,): (i64,) = sqlx::query_as(
        r#"SELECT COUNT(*) FROM "AuditLogs" WHERE "Username" = $1 AND "UserId" IS NULL"#,
    )
    .bind(&email)
    .fetch_one(&pool)
    .await
    .unwrap();
    assert_eq!(null_user_rows as usize, rows.len());

    assert!(
        sqlx::query_as::<_, (Uuid,)>(r#"SELECT "Id" FROM "Users" WHERE "Id" = $1"#)
            .bind(user_id)
            .fetch_optional(&pool)
            .await
            .unwrap()
            .is_some()
    );
}
