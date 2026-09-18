//! Membres et invitations — portage de `HouseMemberService`.
//!
//! Le module réunit les contrôles d'accès (« Access checks », utilisés par tous les
//! autres services) et les points d'entrée membres/invitations du même service C#,
//! pour garder une seule définition du RBAC.

use chrono::{DateTime, Duration, Utc};
use rand::rngs::OsRng;
use rand::RngCore;
use serde_json::Value;
use sqlx::{FromRow, PgPool, Postgres, Transaction};
use uuid::Uuid;

use crate::audit::{self, AuditContext};
use crate::clock;
use crate::dto::members::{
    AcceptInvitationResponse, AllCollaboratorsResponse, HouseCollaboratorsDto, HouseMemberDto,
    InvitationDto, InvitationInfoDto, UpdateMemberPermissionsRequest,
};
use crate::error::{AppError, AppResult};
use crate::models::{HouseRole, InvitationStatus};

/// Nombre maximal d'invitations en attente par maison
/// (`MaxPendingInvitationsPerHouse`).
pub const MAX_PENDING_INVITATIONS_PER_HOUSE: i64 = 20;

/// Durée de validité d'une invitation (`DateTime.UtcNow.AddDays(7)`).
pub const INVITATION_LIFETIME_DAYS: i64 = 7;

/// Tous les rôles : lecture d'une maison ouverte à tout membre.
pub const ANY_ROLE: &[HouseRole] = &[
    HouseRole::Owner,
    HouseRole::CollaboratorRW,
    HouseRole::CollaboratorRO,
    HouseRole::Tenant,
];

/// Rôles autorisés à modifier le contenu d'une maison (appareils, maintenances).
pub const WRITERS: &[HouseRole] = &[HouseRole::Owner, HouseRole::CollaboratorRW];

/// Rôle de l'utilisateur dans une maison, `None` s'il n'y a pas accès.
///
/// La propriété directe (`"Houses"."UserId"`) prime, pour rester compatible avec les
/// maisons antérieures à la table `"HouseMembers"`.
pub async fn get_user_role(
    pool: &PgPool,
    house_id: Uuid,
    user_id: Uuid,
) -> AppResult<Option<HouseRole>> {
    let is_owner: Option<(i32,)> =
        sqlx::query_as(r#"SELECT 1 FROM "Houses" WHERE "Id" = $1 AND "UserId" = $2"#)
            .bind(house_id)
            .bind(user_id)
            .fetch_optional(pool)
            .await?;

    if is_owner.is_some() {
        return Ok(Some(HouseRole::Owner));
    }

    let role: Option<(String,)> = sqlx::query_as(
        r#"SELECT "Role" FROM "HouseMembers" WHERE "HouseId" = $1 AND "UserId" = $2"#,
    )
    .bind(house_id)
    .bind(user_id)
    .fetch_optional(pool)
    .await?;

    Ok(role.and_then(|(role,)| role.parse::<HouseRole>().ok()))
}

/// `EnsureAccessAsync` : 403 corps vide si le rôle n'est pas dans la liste autorisée.
pub async fn ensure_access(
    pool: &PgPool,
    house_id: Uuid,
    user_id: Uuid,
    allowed_roles: &[HouseRole],
) -> AppResult<HouseRole> {
    match get_user_role(pool, house_id, user_id).await? {
        Some(role) if allowed_roles.contains(&role) => Ok(role),
        // UnauthorizedAccessException("Access denied to this house") ⇒ ForbidResult.
        _ => Err(AppError::Forbidden),
    }
}

/// Raccourci : l'utilisateur doit être membre de la maison, quel que soit son rôle.
pub async fn ensure_member(pool: &PgPool, house_id: Uuid, user_id: Uuid) -> AppResult<HouseRole> {
    ensure_access(pool, house_id, user_id, ANY_ROLE).await
}

/// Raccourci : l'utilisateur doit être propriétaire de la maison.
pub async fn ensure_owner(pool: &PgPool, house_id: Uuid, user_id: Uuid) -> AppResult<HouseRole> {
    ensure_access(pool, house_id, user_id, &[HouseRole::Owner]).await
}

/// Raccourci : l'utilisateur doit pouvoir écrire dans la maison.
pub async fn ensure_writer(pool: &PgPool, house_id: Uuid, user_id: Uuid) -> AppResult<HouseRole> {
    ensure_access(pool, house_id, user_id, WRITERS).await
}

/// `CanLogMaintenanceAsync` : propriétaire et collaborateur RW toujours, locataire
/// seulement si sa permission `CanLogMaintenance` est posée.
pub async fn can_log_maintenance(pool: &PgPool, house_id: Uuid, user_id: Uuid) -> AppResult<bool> {
    let Some(role) = get_user_role(pool, house_id, user_id).await? else {
        return Ok(false);
    };

    Ok(match role {
        HouseRole::Owner | HouseRole::CollaboratorRW => true,
        HouseRole::Tenant => {
            let allowed: Option<(i32,)> = sqlx::query_as(
                r#"SELECT 1 FROM "HouseMembers"
                    WHERE "HouseId" = $1 AND "UserId" = $2 AND "CanLogMaintenance""#,
            )
            .bind(house_id)
            .bind(user_id)
            .fetch_optional(pool)
            .await?;
            allowed.is_some()
        }
        HouseRole::CollaboratorRO => false,
    })
}

/// `EnsureCanLogMaintenance` : 403 corps vide si l'utilisateur ne peut pas consigner
/// une maintenance.
pub async fn ensure_can_log_maintenance(
    pool: &PgPool,
    house_id: Uuid,
    user_id: Uuid,
) -> AppResult<()> {
    if can_log_maintenance(pool, house_id, user_id).await? {
        Ok(())
    } else {
        Err(AppError::Forbidden)
    }
}

/// `ShouldHideCostsAsync` : les coûts sont masqués aux locataires sans permission,
/// et à tout non-membre.
pub async fn should_hide_costs(pool: &PgPool, house_id: Uuid, user_id: Uuid) -> AppResult<bool> {
    let Some(role) = get_user_role(pool, house_id, user_id).await? else {
        return Ok(true);
    };

    if matches!(
        role,
        HouseRole::Owner | HouseRole::CollaboratorRW | HouseRole::CollaboratorRO
    ) {
        return Ok(false);
    }

    let can_view: Option<(bool,)> = sqlx::query_as(
        r#"SELECT "CanViewCosts" FROM "HouseMembers" WHERE "HouseId" = $1 AND "UserId" = $2"#,
    )
    .bind(house_id)
    .bind(user_id)
    .fetch_optional(pool)
    .await?;

    Ok(match can_view {
        Some((can_view,)) => !can_view,
        None => true,
    })
}

// --- Membres ---------------------------------------------------------------

/// Ligne « membre + utilisateur » : `HouseMember` avec sa navigation `User`.
#[derive(Debug, Clone, FromRow)]
#[sqlx(rename_all = "PascalCase")]
struct MemberRow {
    id: Uuid,
    #[sqlx(try_from = "String")]
    role: HouseRole,
    can_log_maintenance: bool,
    can_view_costs: bool,
    created_at: DateTime<Utc>,
    user_id: Uuid,
    house_id: Uuid,
    first_name: String,
    last_name: String,
    email: String,
}

/// Colonnes du membre et de l'utilisateur associé, dans l'ordre attendu par
/// [`MemberRow`].
const MEMBER_COLUMNS: &str = r#"m."Id", m."Role", m."CanLogMaintenance", m."CanViewCosts",
        m."CreatedAt", m."UserId", m."HouseId",
        u."FirstName", u."LastName", u."Email""#;

impl From<&MemberRow> for HouseMemberDto {
    fn from(row: &MemberRow) -> Self {
        Self {
            id: row.id,
            user_id: row.user_id,
            first_name: row.first_name.clone(),
            last_name: row.last_name.clone(),
            email: row.email.clone(),
            role: row.role.to_string(),
            can_log_maintenance: row.can_log_maintenance,
            can_view_costs: row.can_view_costs,
            created_at: row.created_at,
        }
    }
}

/// `GetHouseMembersAsync` : tous les membres de la maison, propriétaire compris.
pub async fn get_house_members(
    pool: &PgPool,
    house_id: Uuid,
    user_id: Uuid,
) -> AppResult<Vec<HouseMemberDto>> {
    ensure_member(pool, house_id, user_id).await?;

    let members: Vec<MemberRow> = sqlx::query_as(&format!(
        r#"SELECT {MEMBER_COLUMNS}
             FROM "HouseMembers" m
             JOIN "Users" u ON u."Id" = m."UserId"
            WHERE m."HouseId" = $1"#
    ))
    .bind(house_id)
    .fetch_all(pool)
    .await?;

    Ok(members.iter().map(HouseMemberDto::from).collect())
}

/// Membre unique, avec son utilisateur — `None` si l'identifiant est inconnu.
async fn find_member(pool: &PgPool, member_id: Uuid) -> AppResult<Option<MemberRow>> {
    Ok(sqlx::query_as(&format!(
        r#"SELECT {MEMBER_COLUMNS}
             FROM "HouseMembers" m
             JOIN "Users" u ON u."Id" = m."UserId"
            WHERE m."Id" = $1"#
    ))
    .bind(member_id)
    .fetch_optional(pool)
    .await?)
}

/// `UpdateMemberRoleAsync` : seul le propriétaire change un rôle, jamais le sien ni
/// vers `Owner`. `None` ⇒ membre inconnu (404 corps vide côté contrôleur).
pub async fn update_member_role(
    pool: &PgPool,
    context: &AuditContext,
    member_id: Uuid,
    new_role: HouseRole,
    user_id: Uuid,
) -> AppResult<Option<HouseMemberDto>> {
    let Some(member) = find_member(pool, member_id).await? else {
        return Ok(None);
    };

    ensure_owner(pool, member.house_id, user_id).await?;

    if member.role == HouseRole::Owner {
        return Err(AppError::BadRequest(
            "Cannot change the owner's role".to_string(),
        ));
    }
    if new_role == HouseRole::Owner {
        return Err(AppError::BadRequest("Cannot assign owner role".to_string()));
    }

    // Quitter le rôle de locataire redonne le droit de consigner une maintenance.
    let can_log_maintenance = if new_role == HouseRole::Tenant {
        member.can_log_maintenance
    } else {
        true
    };

    let now = clock::now();
    let mut tx = pool.begin().await?;

    sqlx::query(
        r#"UPDATE "HouseMembers"
              SET "Role" = $1, "CanLogMaintenance" = $2, "UpdatedAt" = $3
            WHERE "Id" = $4"#,
    )
    .bind(new_role.to_string())
    .bind(can_log_maintenance)
    .bind(now)
    .bind(member_id)
    .execute(&mut *tx)
    .await?;

    // Comme EF, seules les propriétés réellement modifiées sont journalisées.
    let mut old = audit::values([("Role", Value::String(member.role.to_string()))]);
    let mut new = audit::values([("Role", Value::String(new_role.to_string()))]);
    let mut changed = vec!["Role"];
    if can_log_maintenance != member.can_log_maintenance {
        old.insert(
            "CanLogMaintenance".to_string(),
            Value::Bool(member.can_log_maintenance),
        );
        new.insert(
            "CanLogMaintenance".to_string(),
            Value::Bool(can_log_maintenance),
        );
        changed.push("CanLogMaintenance");
    }
    old.insert("UpdatedAt".to_string(), Value::Null);
    new.insert("UpdatedAt".to_string(), audit::date(now));
    changed.push("UpdatedAt");

    audit::record_modified(
        &mut tx,
        context,
        "HouseMember",
        member_id,
        &old,
        &new,
        &changed,
    )
    .await?;
    tx.commit().await?;

    Ok(Some(HouseMemberDto {
        role: new_role.to_string(),
        can_log_maintenance,
        ..HouseMemberDto::from(&member)
    }))
}

/// `UpdateMemberPermissionsAsync` : permissions réservées aux locataires. `false` ⇒
/// membre inconnu (404 corps vide).
pub async fn update_member_permissions(
    pool: &PgPool,
    context: &AuditContext,
    member_id: Uuid,
    request: &UpdateMemberPermissionsRequest,
    user_id: Uuid,
) -> AppResult<bool> {
    let Some(member) = find_member(pool, member_id).await? else {
        return Ok(false);
    };

    ensure_owner(pool, member.house_id, user_id).await?;

    if member.role != HouseRole::Tenant {
        return Err(AppError::BadRequest(
            "Permissions are only configurable for tenants".to_string(),
        ));
    }

    let can_log_maintenance = request
        .can_log_maintenance
        .unwrap_or(member.can_log_maintenance);
    let can_view_costs = request.can_view_costs.unwrap_or(member.can_view_costs);

    let now = clock::now();
    let mut tx = pool.begin().await?;

    sqlx::query(
        r#"UPDATE "HouseMembers"
              SET "CanLogMaintenance" = $1, "CanViewCosts" = $2, "UpdatedAt" = $3
            WHERE "Id" = $4"#,
    )
    .bind(can_log_maintenance)
    .bind(can_view_costs)
    .bind(now)
    .bind(member_id)
    .execute(&mut *tx)
    .await?;

    let mut old = audit::Values::new();
    let mut new = audit::Values::new();
    let mut changed = Vec::new();
    if can_log_maintenance != member.can_log_maintenance {
        old.insert(
            "CanLogMaintenance".to_string(),
            Value::Bool(member.can_log_maintenance),
        );
        new.insert(
            "CanLogMaintenance".to_string(),
            Value::Bool(can_log_maintenance),
        );
        changed.push("CanLogMaintenance");
    }
    if can_view_costs != member.can_view_costs {
        old.insert(
            "CanViewCosts".to_string(),
            Value::Bool(member.can_view_costs),
        );
        new.insert("CanViewCosts".to_string(), Value::Bool(can_view_costs));
        changed.push("CanViewCosts");
    }
    old.insert("UpdatedAt".to_string(), Value::Null);
    new.insert("UpdatedAt".to_string(), audit::date(now));
    changed.push("UpdatedAt");

    audit::record_modified(
        &mut tx,
        context,
        "HouseMember",
        member_id,
        &old,
        &new,
        &changed,
    )
    .await?;
    tx.commit().await?;

    Ok(true)
}

/// `RemoveMemberAsync` : le propriétaire retire un membre, mais jamais lui-même.
pub async fn remove_member(
    pool: &PgPool,
    context: &AuditContext,
    member_id: Uuid,
    user_id: Uuid,
) -> AppResult<bool> {
    let Some(member) = find_member(pool, member_id).await? else {
        return Ok(false);
    };

    ensure_owner(pool, member.house_id, user_id).await?;

    if member.user_id == user_id {
        return Err(AppError::BadRequest(
            "Cannot remove yourself from the house".to_string(),
        ));
    }

    let mut tx = pool.begin().await?;

    sqlx::query(r#"DELETE FROM "HouseMembers" WHERE "Id" = $1"#)
        .bind(member_id)
        .execute(&mut *tx)
        .await?;

    audit::record_deleted(
        &mut tx,
        context,
        "HouseMember",
        member_id,
        &audit::values([
            ("Id", audit::id(member.id)),
            ("Role", Value::String(member.role.to_string())),
            ("CanLogMaintenance", Value::Bool(member.can_log_maintenance)),
            ("CanViewCosts", Value::Bool(member.can_view_costs)),
            ("CreatedAt", audit::date(member.created_at)),
            ("UserId", audit::id(member.user_id)),
            ("HouseId", audit::id(member.house_id)),
        ]),
    )
    .await?;

    tx.commit().await?;
    Ok(true)
}

/// `GetAllCollaboratorsAsync` : pour chaque maison **possédée**, ses membres hors
/// propriétaire et ses invitations en attente non expirées.
pub async fn get_all_collaborators(
    pool: &PgPool,
    user_id: Uuid,
) -> AppResult<AllCollaboratorsResponse> {
    let houses: Vec<(Uuid, String)> =
        sqlx::query_as(r#"SELECT "Id", "Name" FROM "Houses" WHERE "UserId" = $1"#)
            .bind(user_id)
            .fetch_all(pool)
            .await?;

    let mut result = Vec::with_capacity(houses.len());
    let now = clock::now();

    for (house_id, house_name) in houses {
        let members: Vec<MemberRow> = sqlx::query_as(&format!(
            r#"SELECT {MEMBER_COLUMNS}
                 FROM "HouseMembers" m
                 JOIN "Users" u ON u."Id" = m."UserId"
                WHERE m."HouseId" = $1 AND m."Role" <> $2"#
        ))
        .bind(house_id)
        .bind(HouseRole::Owner.to_string())
        .fetch_all(pool)
        .await?;

        let invitations = pending_invitations(pool, house_id, &house_name, now, false).await?;

        result.push(HouseCollaboratorsDto {
            house_id,
            house_name,
            members: members.iter().map(HouseMemberDto::from).collect(),
            pending_invitations: invitations,
        });
    }

    Ok(AllCollaboratorsResponse { houses: result })
}

// --- Invitations -----------------------------------------------------------

/// Ligne « invitation + créateur », pour les vues qui affichent le nom de l'invitant.
#[derive(Debug, Clone, FromRow)]
#[sqlx(rename_all = "PascalCase")]
struct InvitationRow {
    id: Uuid,
    token: String,
    #[sqlx(try_from = "String")]
    role: HouseRole,
    #[sqlx(try_from = "String")]
    status: InvitationStatus,
    expires_at: DateTime<Utc>,
    created_at: DateTime<Utc>,
    house_id: Uuid,
    created_by_first_name: String,
    created_by_last_name: String,
}

const INVITATION_COLUMNS: &str = r#"i."Id", i."Token", i."Role", i."Status", i."ExpiresAt",
        i."CreatedAt", i."HouseId",
        u."FirstName" AS "CreatedByFirstName", u."LastName" AS "CreatedByLastName""#;

impl InvitationRow {
    /// `ToInvitationDto` : `redact_token` masque le jeton pour un appelant qui n'est
    /// pas propriétaire de la maison.
    fn to_dto(&self, house_name: &str, redact_token: bool) -> InvitationDto {
        InvitationDto {
            id: self.id,
            token: if redact_token {
                String::new()
            } else {
                self.token.clone()
            },
            role: self.role.to_string(),
            status: self.status.to_string(),
            house_id: self.house_id,
            house_name: house_name.to_string(),
            created_by_name: format!(
                "{} {}",
                self.created_by_first_name, self.created_by_last_name
            )
            .trim()
            .to_string(),
            expires_at: self.expires_at,
            created_at: self.created_at,
        }
    }
}

/// Invitations en attente et non expirées d'une maison, de la plus récente à la plus
/// ancienne.
async fn pending_invitations(
    pool: &PgPool,
    house_id: Uuid,
    house_name: &str,
    now: DateTime<Utc>,
    redact_token: bool,
) -> AppResult<Vec<InvitationDto>> {
    let rows: Vec<InvitationRow> = sqlx::query_as(&format!(
        r#"SELECT {INVITATION_COLUMNS}
             FROM "Invitations" i
             JOIN "Users" u ON u."Id" = i."CreatedByUserId"
            WHERE i."HouseId" = $1 AND i."Status" = $2 AND i."ExpiresAt" > $3
            ORDER BY i."CreatedAt" DESC"#
    ))
    .bind(house_id)
    .bind(InvitationStatus::Pending.to_string())
    .bind(now)
    .fetch_all(pool)
    .await?;

    Ok(rows
        .iter()
        .map(|row| row.to_dto(house_name, redact_token))
        .collect())
}

/// Jeton d'invitation : 32 octets aléatoires en hexadécimal minuscule
/// (`Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant()`).
pub fn generate_token() -> String {
    let mut bytes = [0u8; 32];
    OsRng.fill_bytes(&mut bytes);
    hex::encode(bytes)
}

/// `CreateInvitationAsync` : le propriétaire invite n'importe quel rôle, le
/// collaborateur RW uniquement des locataires.
pub async fn create_invitation(
    pool: &PgPool,
    context: &AuditContext,
    house_id: Uuid,
    role: HouseRole,
    user_id: Uuid,
) -> AppResult<InvitationDto> {
    if role == HouseRole::Owner {
        return Err(AppError::BadRequest(
            "Cannot create invitation for owner role".to_string(),
        ));
    }

    let caller_role = get_user_role(pool, house_id, user_id)
        .await?
        .ok_or(AppError::Forbidden)?;

    let allowed = match caller_role {
        HouseRole::Owner => true,
        HouseRole::CollaboratorRW => role == HouseRole::Tenant,
        _ => false,
    };
    if !allowed {
        // UnauthorizedAccessException("You don't have permission…") ⇒ ForbidResult.
        return Err(AppError::Forbidden);
    }

    let house: Option<(String,)> = sqlx::query_as(r#"SELECT "Name" FROM "Houses" WHERE "Id" = $1"#)
        .bind(house_id)
        .fetch_optional(pool)
        .await?;
    let house_name = house
        .ok_or_else(|| AppError::NotFound("House not found".to_string()))?
        .0;

    let now = clock::now();
    let (pending_count,): (i64,) = sqlx::query_as(
        r#"SELECT COUNT(*) FROM "Invitations"
            WHERE "HouseId" = $1 AND "Status" = $2 AND "ExpiresAt" > $3"#,
    )
    .bind(house_id)
    .bind(InvitationStatus::Pending.to_string())
    .bind(now)
    .fetch_one(pool)
    .await?;

    if pending_count >= MAX_PENDING_INVITATIONS_PER_HOUSE {
        return Err(AppError::BadRequest(format!(
            "Maximum of {MAX_PENDING_INVITATIONS_PER_HOUSE} pending invitations per house reached"
        )));
    }

    let invitation_id = Uuid::new_v4();
    let token = generate_token();
    let expires_at = now + Duration::days(INVITATION_LIFETIME_DAYS);

    let mut tx = pool.begin().await?;

    sqlx::query(
        r#"INSERT INTO "Invitations"
               ("Id", "Token", "Role", "Status", "ExpiresAt", "CreatedAt", "HouseId", "CreatedByUserId")
           VALUES ($1, $2, $3, $4, $5, $6, $7, $8)"#,
    )
    .bind(invitation_id)
    .bind(&token)
    .bind(role.to_string())
    .bind(InvitationStatus::Pending.to_string())
    .bind(expires_at)
    .bind(now)
    .bind(house_id)
    .bind(user_id)
    .execute(&mut *tx)
    .await?;

    audit::record_added(
        &mut tx,
        context,
        "Invitation",
        invitation_id,
        &audit::values([
            ("Id", audit::id(invitation_id)),
            ("Token", Value::String(token.clone())),
            ("Role", Value::String(role.to_string())),
            (
                "Status",
                Value::String(InvitationStatus::Pending.to_string()),
            ),
            ("ExpiresAt", audit::date(expires_at)),
            ("CreatedAt", audit::date(now)),
            ("AcceptedAt", Value::Null),
            ("RevokedAt", Value::Null),
            ("HouseId", audit::id(house_id)),
            ("CreatedByUserId", audit::id(user_id)),
            ("AcceptedByUserId", Value::Null),
        ]),
    )
    .await?;

    tx.commit().await?;

    // Le C# recharge le créateur pour composer `CreatedByName` : c'est l'appelant.
    let creator: Option<(String, String)> =
        sqlx::query_as(r#"SELECT "FirstName", "LastName" FROM "Users" WHERE "Id" = $1"#)
            .bind(user_id)
            .fetch_optional(pool)
            .await?;
    let created_by_name = creator
        .map(|(first, last)| format!("{first} {last}").trim().to_string())
        .unwrap_or_default();

    Ok(InvitationDto {
        id: invitation_id,
        token,
        role: role.to_string(),
        status: InvitationStatus::Pending.to_string(),
        house_id,
        house_name,
        created_by_name,
        expires_at,
        created_at: now,
    })
}

/// `GetHouseInvitationsAsync` : propriétaire et collaborateur RW seulement ; le jeton
/// n'est renvoyé qu'au propriétaire.
pub async fn get_house_invitations(
    pool: &PgPool,
    house_id: Uuid,
    user_id: Uuid,
) -> AppResult<Vec<InvitationDto>> {
    let caller_role = get_user_role(pool, house_id, user_id).await?;
    let is_owner = match caller_role {
        Some(HouseRole::Owner) => true,
        Some(HouseRole::CollaboratorRW) => false,
        _ => return Err(AppError::Forbidden),
    };

    let house: Option<(String,)> = sqlx::query_as(r#"SELECT "Name" FROM "Houses" WHERE "Id" = $1"#)
        .bind(house_id)
        .fetch_optional(pool)
        .await?;
    let house_name = house
        .ok_or_else(|| AppError::NotFound("House not found".to_string()))?
        .0;

    pending_invitations(pool, house_id, &house_name, clock::now(), !is_owner).await
}

/// Ligne servant le point d'entrée anonyme : l'invitation et le nom de sa maison.
#[derive(Debug, Clone, FromRow)]
#[sqlx(rename_all = "PascalCase")]
struct InvitationInfoRow {
    id: Uuid,
    role: String,
    status: String,
    expires_at: DateTime<Utc>,
    house_name: Option<String>,
}

/// `GetInvitationInfoAsync` : point d'entrée anonyme, volontairement avare en
/// informations. `None` ⇒ jeton inconnu (404 corps vide).
pub async fn get_invitation_info(
    pool: &PgPool,
    token: &str,
) -> AppResult<Option<InvitationInfoDto>> {
    let row: Option<InvitationInfoRow> = sqlx::query_as(
        r#"SELECT i."Id", i."Role", i."Status", i."ExpiresAt", h."Name" AS "HouseName"
             FROM "Invitations" i
             LEFT JOIN "Houses" h ON h."Id" = i."HouseId"
            WHERE i."Token" = $1"#,
    )
    .bind(token)
    .fetch_optional(pool)
    .await?;

    let Some(row) = row else {
        return Ok(None);
    };

    let status = row
        .status
        .parse::<InvitationStatus>()
        .unwrap_or(InvitationStatus::Pending);

    Ok(Some(InvitationInfoDto {
        id: row.id,
        house_name: row.house_name.unwrap_or_default(),
        role: row.role,
        // Le C# ne charge pas `CreatedByUser` sur cette requête : la navigation est
        // nulle et `FormatInviterName` renvoie une chaîne vide. Reproduit tel quel.
        invited_by_name: String::new(),
        expires_at: row.expires_at,
        is_expired: status != InvitationStatus::Pending || row.expires_at <= clock::now(),
    }))
}

/// Invitation verrouillée pendant l'acceptation.
#[derive(Debug, Clone, FromRow)]
#[sqlx(rename_all = "PascalCase")]
struct AcceptRow {
    id: Uuid,
    role: String,
    status: String,
    expires_at: DateTime<Utc>,
    house_id: Uuid,
    created_by_user_id: Uuid,
}

/// `AcceptInvitationAsync`.
///
/// Le C# sérialise la transaction pour éviter la course entre deux acceptations ;
/// ici le verrou de ligne sur l'invitation (`FOR UPDATE`) joue le même rôle, et
/// l'index unique `IX_HouseMembers_UserId_HouseId` reste le juge de paix.
pub async fn accept_invitation(
    pool: &PgPool,
    context: &AuditContext,
    token: &str,
    user_id: Uuid,
) -> AppResult<AcceptInvitationResponse> {
    let now = clock::now();
    let mut tx = pool.begin().await?;

    let invitation: Option<AcceptRow> = sqlx::query_as(
        r#"SELECT "Id", "Role", "Status", "ExpiresAt", "HouseId", "CreatedByUserId"
             FROM "Invitations"
            WHERE "Token" = $1
              FOR UPDATE"#,
    )
    .bind(token)
    .fetch_optional(&mut *tx)
    .await?;

    let Some(invitation) = invitation else {
        return Err(AppError::NotFound("Invitation not found".to_string()));
    };

    let status = invitation
        .status
        .parse::<InvitationStatus>()
        .unwrap_or(InvitationStatus::Pending);
    if status != InvitationStatus::Pending {
        return Err(AppError::BadRequest(
            "This invitation is no longer valid".to_string(),
        ));
    }

    // Invitation périmée : le C# la marque `Expired` et **valide** la transaction
    // avant de lever l'erreur.
    if invitation.expires_at <= now {
        sqlx::query(r#"UPDATE "Invitations" SET "Status" = $1 WHERE "Id" = $2"#)
            .bind(InvitationStatus::Expired.to_string())
            .bind(invitation.id)
            .execute(&mut *tx)
            .await?;

        audit::record_modified(
            &mut tx,
            context,
            "Invitation",
            invitation.id,
            &audit::values([(
                "Status",
                Value::String(InvitationStatus::Pending.to_string()),
            )]),
            &audit::values([(
                "Status",
                Value::String(InvitationStatus::Expired.to_string()),
            )]),
            &["Status"],
        )
        .await?;

        tx.commit().await?;
        return Err(AppError::BadRequest(
            "This invitation has expired".to_string(),
        ));
    }

    if invitation.created_by_user_id == user_id {
        return Err(AppError::BadRequest(
            "You cannot accept your own invitation".to_string(),
        ));
    }

    let existing: Option<(i32,)> =
        sqlx::query_as(r#"SELECT 1 FROM "HouseMembers" WHERE "HouseId" = $1 AND "UserId" = $2"#)
            .bind(invitation.house_id)
            .bind(user_id)
            .fetch_optional(&mut *tx)
            .await?;

    if existing.is_some() {
        return Err(already_a_member());
    }

    let role = invitation
        .role
        .parse::<HouseRole>()
        .unwrap_or(HouseRole::Tenant);
    insert_membership(&mut tx, context, user_id, invitation.house_id, role, now).await?;

    sqlx::query(
        r#"UPDATE "Invitations"
              SET "Status" = $1, "AcceptedByUserId" = $2, "AcceptedAt" = $3
            WHERE "Id" = $4"#,
    )
    .bind(InvitationStatus::Accepted.to_string())
    .bind(user_id)
    .bind(now)
    .bind(invitation.id)
    .execute(&mut *tx)
    .await?;

    audit::record_modified(
        &mut tx,
        context,
        "Invitation",
        invitation.id,
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
    .await?;

    tx.commit().await?;

    let house_name: Option<(String,)> =
        sqlx::query_as(r#"SELECT "Name" FROM "Houses" WHERE "Id" = $1"#)
            .bind(invitation.house_id)
            .fetch_optional(pool)
            .await?;

    Ok(AcceptInvitationResponse {
        house_id: invitation.house_id,
        house_name: house_name.map(|(name,)| name).unwrap_or_default(),
        role: role.to_string(),
    })
}

fn already_a_member() -> AppError {
    AppError::BadRequest("You are already a member of this house".to_string())
}

/// Crée l'appartenance issue d'une invitation acceptée, avec sa ligne d'audit.
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
    .await
    .map_err(|error| match error {
        // Deux acceptations simultanées : l'index unique tranche, le message reste
        // celui du C#.
        sqlx::Error::Database(ref db)
            if db.constraint() == Some("IX_HouseMembers_UserId_HouseId") =>
        {
            already_a_member()
        }
        other => other.into(),
    })?;

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

/// `RevokeInvitationAsync` : le créateur de l'invitation ou le propriétaire de la
/// maison. `false` ⇒ invitation inconnue (404 corps vide).
pub async fn revoke_invitation(
    pool: &PgPool,
    context: &AuditContext,
    invitation_id: Uuid,
    user_id: Uuid,
) -> AppResult<bool> {
    let invitation: Option<(String, Uuid, Uuid)> = sqlx::query_as(
        r#"SELECT "Status", "HouseId", "CreatedByUserId" FROM "Invitations" WHERE "Id" = $1"#,
    )
    .bind(invitation_id)
    .fetch_optional(pool)
    .await?;

    let Some((status, house_id, created_by_user_id)) = invitation else {
        return Ok(false);
    };

    // Le C# vérifie le statut **avant** les droits : une invitation déjà traitée
    // répond 400, même à un inconnu.
    let status = status
        .parse::<InvitationStatus>()
        .unwrap_or(InvitationStatus::Pending);
    if status != InvitationStatus::Pending {
        return Err(AppError::BadRequest(
            "Only pending invitations can be revoked".to_string(),
        ));
    }

    let is_creator = created_by_user_id == user_id;
    let is_owner = !is_creator
        && matches!(
            get_user_role(pool, house_id, user_id).await?,
            Some(HouseRole::Owner)
        );

    if !is_creator && !is_owner {
        return Err(AppError::Forbidden);
    }

    let now = clock::now();
    let mut tx = pool.begin().await?;

    sqlx::query(r#"UPDATE "Invitations" SET "Status" = $1, "RevokedAt" = $2 WHERE "Id" = $3"#)
        .bind(InvitationStatus::Revoked.to_string())
        .bind(now)
        .bind(invitation_id)
        .execute(&mut *tx)
        .await?;

    audit::record_modified(
        &mut tx,
        context,
        "Invitation",
        invitation_id,
        &audit::values([
            (
                "Status",
                Value::String(InvitationStatus::Pending.to_string()),
            ),
            ("RevokedAt", Value::Null),
        ]),
        &audit::values([
            (
                "Status",
                Value::String(InvitationStatus::Revoked.to_string()),
            ),
            ("RevokedAt", audit::date(now)),
        ]),
        &["Status", "RevokedAt"],
    )
    .await?;

    tx.commit().await?;
    Ok(true)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn invitation_tokens_are_64_lowercase_hexadecimal_characters() {
        let token = generate_token();
        assert_eq!(token.len(), 64);
        assert!(token
            .chars()
            .all(|c| c.is_ascii_digit() || ('a'..='f').contains(&c)));
        assert_ne!(token, generate_token());
    }

    #[test]
    fn role_sets_match_the_csharp_call_sites() {
        assert_eq!(WRITERS, &[HouseRole::Owner, HouseRole::CollaboratorRW]);
        assert_eq!(ANY_ROLE.len(), 4);
        assert!(ANY_ROLE.contains(&HouseRole::Tenant));
    }
}
