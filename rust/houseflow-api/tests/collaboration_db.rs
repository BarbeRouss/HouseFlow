//! Tests des services membres / invitations / administration **avec** une vraie base
//! PostgreSQL.
//!
//! Ils sont `#[ignore]` : `cargo test` seul reste hors-ligne. Pour les jouer :
//!
//! ```bash
//! DATABASE_URL=postgres://postgres:postgres@localhost:5432/houseflow_rust_tests \
//!   cargo test -p houseflow-api --test collaboration_db -- --ignored --test-threads=1
//! ```
//!
//! Chaque test crée ses propres comptes : aucun état initial n'est supposé.

use chrono::{Duration, Utc};
use houseflow_api::audit::AuditContext;
use houseflow_api::config::{Config, SameSite};
use houseflow_api::db;
use houseflow_api::dto::auth::RegisterRequest;
use houseflow_api::dto::members::UpdateMemberPermissionsRequest;
use houseflow_api::error::AppError;
use houseflow_api::models::{HouseRole, InvitationStatus};
use houseflow_api::services::{admin, auth, members};
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

fn context() -> AuditContext {
    AuditContext::default()
}

/// Inscrit un compte et renvoie `(user_id, house_id)` de sa maison par défaut.
async fn register(pool: &PgPool) -> (Uuid, Uuid) {
    let email = format!("collab-{}@example.com", Uuid::new_v4());
    let request: RegisterRequest = serde_json::from_value(serde_json::json!({
        "email": email,
        "firstName": "Test",
        "lastName": "User",
        "password": PASSWORD,
    }))
    .unwrap();

    let outcome = auth::register(pool, &config(), &request, None, None)
        .await
        .expect("registration should succeed");
    let user_id = outcome.response.user.id;

    let (house_id,): (Uuid,) = sqlx::query_as(r#"SELECT "Id" FROM "Houses" WHERE "UserId" = $1"#)
        .bind(user_id)
        .fetch_one(pool)
        .await
        .unwrap();

    (user_id, house_id)
}

/// Invite un nouveau compte dans la maison et lui fait accepter l'invitation.
async fn add_member(pool: &PgPool, house_id: Uuid, owner_id: Uuid, role: HouseRole) -> Uuid {
    let invitation = members::create_invitation(pool, &context(), house_id, role, owner_id)
        .await
        .expect("the owner should be able to invite");

    let (member_user_id, _) = register(pool).await;
    members::accept_invitation(pool, &context(), &invitation.token, member_user_id)
        .await
        .expect("the invitation should be accepted");

    member_user_id
}

/// Identifiant du membre (ligne `HouseMembers`) d'un utilisateur dans une maison.
async fn member_id(pool: &PgPool, house_id: Uuid, user_id: Uuid) -> Uuid {
    sqlx::query_as::<_, (Uuid,)>(
        r#"SELECT "Id" FROM "HouseMembers" WHERE "HouseId" = $1 AND "UserId" = $2"#,
    )
    .bind(house_id)
    .bind(user_id)
    .fetch_one(pool)
    .await
    .unwrap()
    .0
}

// --- Membres ---------------------------------------------------------------

#[tokio::test]
#[ignore = "requires PostgreSQL"]
async fn the_owner_sees_every_member_of_the_house() {
    let pool = pool().await;
    let (owner_id, house_id) = register(&pool).await;
    add_member(&pool, house_id, owner_id, HouseRole::CollaboratorRW).await;

    let list = members::get_house_members(&pool, house_id, owner_id)
        .await
        .unwrap();

    assert_eq!(list.len(), 2);
    assert!(list.iter().any(|m| m.role == "Owner"));
    assert!(list.iter().any(|m| m.role == "CollaboratorRW"));
    // Le DTO porte l'identité de l'utilisateur, jointe depuis « Users ».
    assert!(list.iter().all(|m| !m.email.is_empty()));
}

#[tokio::test]
#[ignore = "requires PostgreSQL"]
async fn a_stranger_cannot_list_the_members() {
    let pool = pool().await;
    let (owner_id, house_id) = register(&pool).await;
    let (stranger_id, _) = register(&pool).await;
    let _ = owner_id;

    let error = members::get_house_members(&pool, house_id, stranger_id)
        .await
        .unwrap_err();
    assert!(matches!(error, AppError::Forbidden), "got {error:?}");
}

#[tokio::test]
#[ignore = "requires PostgreSQL"]
async fn changing_a_role_away_from_tenant_restores_the_maintenance_permission() {
    let pool = pool().await;
    let (owner_id, house_id) = register(&pool).await;
    let tenant_id = add_member(&pool, house_id, owner_id, HouseRole::Tenant).await;
    let tenant_member = member_id(&pool, house_id, tenant_id).await;

    // Le propriétaire retire d'abord le droit de consigner une maintenance…
    members::update_member_permissions(
        &pool,
        &context(),
        tenant_member,
        &UpdateMemberPermissionsRequest {
            can_log_maintenance: Some(false),
            can_view_costs: None,
        },
        owner_id,
    )
    .await
    .unwrap();
    assert!(!members::can_log_maintenance(&pool, house_id, tenant_id)
        .await
        .unwrap());

    // …puis promeut le locataire, ce qui le lui rend.
    let updated = members::update_member_role(
        &pool,
        &context(),
        tenant_member,
        HouseRole::CollaboratorRO,
        owner_id,
    )
    .await
    .unwrap()
    .expect("the member exists");

    assert_eq!(updated.role, "CollaboratorRO");
    assert!(updated.can_log_maintenance);
}

#[tokio::test]
#[ignore = "requires PostgreSQL"]
async fn the_owner_role_is_neither_granted_nor_taken_away() {
    let pool = pool().await;
    let (owner_id, house_id) = register(&pool).await;
    let collaborator_id = add_member(&pool, house_id, owner_id, HouseRole::CollaboratorRW).await;
    let owner_member = member_id(&pool, house_id, owner_id).await;
    let collaborator_member = member_id(&pool, house_id, collaborator_id).await;

    let promote = members::update_member_role(
        &pool,
        &context(),
        collaborator_member,
        HouseRole::Owner,
        owner_id,
    )
    .await
    .unwrap_err();
    assert!(
        matches!(promote, AppError::BadRequest(ref m) if m == "Cannot assign owner role"),
        "got {promote:?}"
    );

    let demote =
        members::update_member_role(&pool, &context(), owner_member, HouseRole::Tenant, owner_id)
            .await
            .unwrap_err();
    assert!(
        matches!(demote, AppError::BadRequest(ref m) if m == "Cannot change the owner's role"),
        "got {demote:?}"
    );
}

#[tokio::test]
#[ignore = "requires PostgreSQL"]
async fn permissions_are_reserved_for_tenants_and_members_cannot_remove_themselves() {
    let pool = pool().await;
    let (owner_id, house_id) = register(&pool).await;
    let collaborator_id = add_member(&pool, house_id, owner_id, HouseRole::CollaboratorRO).await;
    let collaborator_member = member_id(&pool, house_id, collaborator_id).await;
    let owner_member = member_id(&pool, house_id, owner_id).await;

    let error = members::update_member_permissions(
        &pool,
        &context(),
        collaborator_member,
        &UpdateMemberPermissionsRequest::default(),
        owner_id,
    )
    .await
    .unwrap_err();
    assert!(
        matches!(error, AppError::BadRequest(ref m) if m == "Permissions are only configurable for tenants"),
        "got {error:?}"
    );

    let error = members::remove_member(&pool, &context(), owner_member, owner_id)
        .await
        .unwrap_err();
    assert!(
        matches!(error, AppError::BadRequest(ref m) if m == "Cannot remove yourself from the house"),
        "got {error:?}"
    );

    // Un membre inconnu n'est pas une erreur : le contrôleur en fait un 404 vide.
    assert!(
        !members::remove_member(&pool, &context(), Uuid::new_v4(), owner_id)
            .await
            .unwrap()
    );
    assert!(
        members::remove_member(&pool, &context(), collaborator_member, owner_id)
            .await
            .unwrap()
    );
}

#[tokio::test]
#[ignore = "requires PostgreSQL"]
async fn collaborators_list_the_owned_houses_without_their_owner() {
    let pool = pool().await;
    let (owner_id, house_id) = register(&pool).await;
    add_member(&pool, house_id, owner_id, HouseRole::Tenant).await;
    members::create_invitation(
        &pool,
        &context(),
        house_id,
        HouseRole::CollaboratorRW,
        owner_id,
    )
    .await
    .unwrap();

    let result = members::get_all_collaborators(&pool, owner_id)
        .await
        .unwrap();
    let house = result
        .houses
        .iter()
        .find(|h| h.house_id == house_id)
        .expect("the owned house is listed");

    assert_eq!(house.members.len(), 1, "le propriétaire est exclu");
    assert_eq!(house.members[0].role, "Tenant");
    assert_eq!(house.pending_invitations.len(), 1);
    assert!(!house.pending_invitations[0].token.is_empty());
}

// --- Invitations -----------------------------------------------------------

#[tokio::test]
#[ignore = "requires PostgreSQL"]
async fn a_collaborator_rw_may_only_invite_tenants() {
    let pool = pool().await;
    let (owner_id, house_id) = register(&pool).await;
    let collaborator_id = add_member(&pool, house_id, owner_id, HouseRole::CollaboratorRW).await;

    members::create_invitation(
        &pool,
        &context(),
        house_id,
        HouseRole::Tenant,
        collaborator_id,
    )
    .await
    .expect("a collaborator RW invites tenants");

    for role in [HouseRole::CollaboratorRW, HouseRole::CollaboratorRO] {
        let error = members::create_invitation(&pool, &context(), house_id, role, collaborator_id)
            .await
            .unwrap_err();
        assert!(matches!(error, AppError::Forbidden), "got {error:?}");
    }

    // Le rôle de propriétaire ne s'invite jamais, même par le propriétaire.
    let error = members::create_invitation(&pool, &context(), house_id, HouseRole::Owner, owner_id)
        .await
        .unwrap_err();
    assert!(
        matches!(error, AppError::BadRequest(ref m) if m == "Cannot create invitation for owner role"),
        "got {error:?}"
    );
}

#[tokio::test]
#[ignore = "requires PostgreSQL"]
async fn the_token_is_redacted_for_a_caller_who_is_not_the_owner() {
    let pool = pool().await;
    let (owner_id, house_id) = register(&pool).await;
    let collaborator_id = add_member(&pool, house_id, owner_id, HouseRole::CollaboratorRW).await;
    let tenant_id = add_member(&pool, house_id, owner_id, HouseRole::Tenant).await;
    members::create_invitation(&pool, &context(), house_id, HouseRole::Tenant, owner_id)
        .await
        .unwrap();

    let as_owner = members::get_house_invitations(&pool, house_id, owner_id)
        .await
        .unwrap();
    assert!(as_owner.iter().all(|i| i.token.len() == 64));

    let as_collaborator = members::get_house_invitations(&pool, house_id, collaborator_id)
        .await
        .unwrap();
    assert_eq!(as_collaborator.len(), as_owner.len());
    assert!(as_collaborator.iter().all(|i| i.token.is_empty()));

    // Un locataire n'a pas accès à la liste du tout.
    let error = members::get_house_invitations(&pool, house_id, tenant_id)
        .await
        .unwrap_err();
    assert!(matches!(error, AppError::Forbidden), "got {error:?}");
}

#[tokio::test]
#[ignore = "requires PostgreSQL"]
async fn the_anonymous_lookup_reports_an_expired_invitation() {
    let pool = pool().await;
    let (owner_id, house_id) = register(&pool).await;
    let invitation =
        members::create_invitation(&pool, &context(), house_id, HouseRole::Tenant, owner_id)
            .await
            .unwrap();

    let info = members::get_invitation_info(&pool, &invitation.token)
        .await
        .unwrap()
        .expect("the token is known");
    assert_eq!(info.role, "Tenant");
    assert!(!info.is_expired);
    assert!(!info.house_name.is_empty());

    assert!(members::get_invitation_info(&pool, "unknown-token")
        .await
        .unwrap()
        .is_none());

    // Une invitation révoquée est annoncée comme expirée.
    assert!(
        members::revoke_invitation(&pool, &context(), invitation.id, owner_id)
            .await
            .unwrap()
    );
    let info = members::get_invitation_info(&pool, &invitation.token)
        .await
        .unwrap()
        .unwrap();
    assert!(info.is_expired);
}

#[tokio::test]
#[ignore = "requires PostgreSQL"]
async fn accepting_twice_or_accepting_ones_own_invitation_is_refused() {
    let pool = pool().await;
    let (owner_id, house_id) = register(&pool).await;
    let first = members::create_invitation(
        &pool,
        &context(),
        house_id,
        HouseRole::CollaboratorRW,
        owner_id,
    )
    .await
    .unwrap();

    let own = members::accept_invitation(&pool, &context(), &first.token, owner_id)
        .await
        .unwrap_err();
    assert!(
        matches!(own, AppError::BadRequest(ref m) if m == "You cannot accept your own invitation"),
        "got {own:?}"
    );

    let (guest_id, _) = register(&pool).await;
    let accepted = members::accept_invitation(&pool, &context(), &first.token, guest_id)
        .await
        .unwrap();
    assert_eq!(accepted.house_id, house_id);
    assert_eq!(accepted.role, "CollaboratorRW");

    // Le même jeton n'est plus « Pending ».
    let replay = members::accept_invitation(&pool, &context(), &first.token, guest_id)
        .await
        .unwrap_err();
    assert!(
        matches!(replay, AppError::BadRequest(ref m) if m == "This invitation is no longer valid"),
        "got {replay:?}"
    );

    // Un second jeton ne fait pas entrer deux fois le même membre.
    let second =
        members::create_invitation(&pool, &context(), house_id, HouseRole::Tenant, owner_id)
            .await
            .unwrap();
    let already = members::accept_invitation(&pool, &context(), &second.token, guest_id)
        .await
        .unwrap_err();
    assert!(
        matches!(already, AppError::BadRequest(ref m) if m == "You are already a member of this house"),
        "got {already:?}"
    );

    let unknown = members::accept_invitation(&pool, &context(), "nope", guest_id)
        .await
        .unwrap_err();
    assert!(
        matches!(unknown, AppError::NotFound(ref m) if m == "Invitation not found"),
        "got {unknown:?}"
    );
}

#[tokio::test]
#[ignore = "requires PostgreSQL"]
async fn an_expired_invitation_is_marked_expired_then_refused() {
    let pool = pool().await;
    let (owner_id, house_id) = register(&pool).await;
    let invitation =
        members::create_invitation(&pool, &context(), house_id, HouseRole::Tenant, owner_id)
            .await
            .unwrap();

    sqlx::query(r#"UPDATE "Invitations" SET "ExpiresAt" = $1 WHERE "Id" = $2"#)
        .bind(Utc::now() - Duration::days(1))
        .bind(invitation.id)
        .execute(&pool)
        .await
        .unwrap();

    let (guest_id, _) = register(&pool).await;
    let error = members::accept_invitation(&pool, &context(), &invitation.token, guest_id)
        .await
        .unwrap_err();
    assert!(
        matches!(error, AppError::BadRequest(ref m) if m == "This invitation has expired"),
        "got {error:?}"
    );

    // Le changement de statut a bien été validé malgré l'erreur.
    let (status,): (String,) =
        sqlx::query_as(r#"SELECT "Status" FROM "Invitations" WHERE "Id" = $1"#)
            .bind(invitation.id)
            .fetch_one(&pool)
            .await
            .unwrap();
    assert_eq!(status, InvitationStatus::Expired.to_string());
}

#[tokio::test]
#[ignore = "requires PostgreSQL"]
async fn only_the_creator_or_the_owner_revokes_a_pending_invitation() {
    let pool = pool().await;
    let (owner_id, house_id) = register(&pool).await;
    let collaborator_id = add_member(&pool, house_id, owner_id, HouseRole::CollaboratorRW).await;
    let (stranger_id, _) = register(&pool).await;

    let invitation = members::create_invitation(
        &pool,
        &context(),
        house_id,
        HouseRole::Tenant,
        collaborator_id,
    )
    .await
    .unwrap();

    let error = members::revoke_invitation(&pool, &context(), invitation.id, stranger_id)
        .await
        .unwrap_err();
    assert!(matches!(error, AppError::Forbidden), "got {error:?}");

    // Le propriétaire de la maison peut révoquer l'invitation d'un collaborateur.
    assert!(
        members::revoke_invitation(&pool, &context(), invitation.id, owner_id)
            .await
            .unwrap()
    );

    // Deux fois de suite : le statut prime sur les droits.
    let error = members::revoke_invitation(&pool, &context(), invitation.id, owner_id)
        .await
        .unwrap_err();
    assert!(
        matches!(error, AppError::BadRequest(ref m) if m == "Only pending invitations can be revoked"),
        "got {error:?}"
    );

    assert!(
        !members::revoke_invitation(&pool, &context(), Uuid::new_v4(), owner_id)
            .await
            .unwrap()
    );
}

#[tokio::test]
#[ignore = "requires PostgreSQL"]
async fn a_house_holds_at_most_twenty_pending_invitations() {
    let pool = pool().await;
    let (owner_id, house_id) = register(&pool).await;

    for _ in 0..members::MAX_PENDING_INVITATIONS_PER_HOUSE {
        members::create_invitation(&pool, &context(), house_id, HouseRole::Tenant, owner_id)
            .await
            .unwrap();
    }

    let error =
        members::create_invitation(&pool, &context(), house_id, HouseRole::Tenant, owner_id)
            .await
            .unwrap_err();
    assert!(
        matches!(error, AppError::BadRequest(ref m) if m.contains("pending invitations per house reached")),
        "got {error:?}"
    );
}

// --- Administration --------------------------------------------------------

#[tokio::test]
#[ignore = "requires PostgreSQL"]
async fn statistics_count_the_whole_platform() {
    let pool = pool().await;
    let before = admin::get_stats(&pool).await.unwrap();
    register(&pool).await;
    let after = admin::get_stats(&pool).await.unwrap();

    assert_eq!(after.users, before.users + 1);
    assert_eq!(after.houses, before.houses + 1);
    assert!(after.admins <= after.users);
}

#[tokio::test]
#[ignore = "requires PostgreSQL"]
async fn the_user_search_is_case_insensitive_and_counts_owned_houses() {
    let pool = pool().await;
    let (user_id, _) = register(&pool).await;
    let (email,): (String,) = sqlx::query_as(r#"SELECT "Email" FROM "Users" WHERE "Id" = $1"#)
        .bind(user_id)
        .fetch_one(&pool)
        .await
        .unwrap();

    let page = admin::get_users(&pool, Some(&email.to_uppercase()), 1, 20)
        .await
        .unwrap();
    assert_eq!(page.total, 1);
    assert_eq!(page.users.len(), 1);
    assert_eq!(page.users[0].id, user_id);
    assert_eq!(page.users[0].houses_count, 1);
    assert!(!page.users[0].is_admin);

    // Pagination bornée, total inchangé d'une page à l'autre.
    let first = admin::get_users(&pool, None, 1, 1).await.unwrap();
    assert_eq!(first.users.len(), 1);
    assert_eq!(first.page_size, 1);
    let clamped = admin::get_users(&pool, None, 0, 1_000).await.unwrap();
    assert_eq!(clamped.page_size, 100);
    assert_eq!(clamped.page, 1);
    assert_eq!(clamped.total, first.total);

    // Les utilisateurs sont triés par e-mail.
    let emails: Vec<&str> = clamped.users.iter().map(|u| u.email.as_str()).collect();
    let mut sorted = emails.clone();
    sorted.sort_unstable();
    assert_eq!(emails, sorted);
}

#[tokio::test]
#[ignore = "requires PostgreSQL"]
async fn admin_rights_are_granted_revoked_and_never_self_revoked() {
    let pool = pool().await;
    let (acting_id, _) = register(&pool).await;
    let (target_id, _) = register(&pool).await;

    let granted = admin::set_admin(&pool, &context(), acting_id, target_id, true)
        .await
        .unwrap();
    assert!(granted.is_admin);
    assert_eq!(granted.houses_count, 1);

    // Idempotent : sans changement de valeur, aucune écriture.
    let again = admin::set_admin(&pool, &context(), acting_id, target_id, true)
        .await
        .unwrap();
    assert!(again.is_admin);

    let revoked = admin::set_admin(&pool, &context(), acting_id, target_id, false)
        .await
        .unwrap();
    assert!(!revoked.is_admin);

    let error = admin::set_admin(&pool, &context(), acting_id, acting_id, false)
        .await
        .unwrap_err();
    assert!(
        matches!(error, AppError::BadRequest(ref m) if m == "You cannot remove your own administrator rights."),
        "got {error:?}"
    );

    let error = admin::set_admin(&pool, &context(), acting_id, Uuid::new_v4(), true)
        .await
        .unwrap_err();
    assert!(
        matches!(error, AppError::NotFound(ref m) if m == "User not found"),
        "got {error:?}"
    );
}

// --- Tâche de fond ---------------------------------------------------------

#[tokio::test]
#[ignore = "requires PostgreSQL"]
async fn the_cleanup_job_expires_then_deletes_old_invitations() {
    let pool = pool().await;
    let (owner_id, house_id) = register(&pool).await;

    let fresh =
        members::create_invitation(&pool, &context(), house_id, HouseRole::Tenant, owner_id)
            .await
            .unwrap();
    let stale =
        members::create_invitation(&pool, &context(), house_id, HouseRole::Tenant, owner_id)
            .await
            .unwrap();
    let ancient =
        members::create_invitation(&pool, &context(), house_id, HouseRole::Tenant, owner_id)
            .await
            .unwrap();

    sqlx::query(r#"UPDATE "Invitations" SET "ExpiresAt" = $1 WHERE "Id" = $2"#)
        .bind(Utc::now() - Duration::hours(1))
        .bind(stale.id)
        .execute(&pool)
        .await
        .unwrap();
    sqlx::query(r#"UPDATE "Invitations" SET "ExpiresAt" = $1, "Status" = $2 WHERE "Id" = $3"#)
        .bind(Utc::now() - Duration::days(31))
        .bind(InvitationStatus::Revoked.to_string())
        .bind(ancient.id)
        .execute(&pool)
        .await
        .unwrap();

    houseflow_api::jobs::cleanup_expired_invitations::execute(&pool)
        .await
        .unwrap();

    let status = |id: Uuid| {
        let pool = pool.clone();
        async move {
            sqlx::query_as::<_, (String,)>(r#"SELECT "Status" FROM "Invitations" WHERE "Id" = $1"#)
                .bind(id)
                .fetch_optional(&pool)
                .await
                .unwrap()
                .map(|(status,)| status)
        }
    };

    assert_eq!(
        status(fresh.id).await.as_deref(),
        Some(InvitationStatus::Pending.to_string().as_str())
    );
    assert_eq!(
        status(stale.id).await.as_deref(),
        Some(InvitationStatus::Expired.to_string().as_str())
    );
    assert_eq!(status(ancient.id).await, None, "supprimée après 30 jours");
}
