//! Contrôles d'accès aux maisons — portage des méthodes « Access checks » de
//! `HouseMemberService`.
//!
//! **Phase 2** : les points d'entrée membres/invitations (`GET collaborators`,
//! `GET houses/{houseId}/members`, `PUT members/{memberId}/role`, invitations…) et le
//! reste de `HouseMemberService` ne sont **pas** portés ici. Ce module ne contient que
//! les vérifications dont les autres services ont besoin ; les ajouter ici plutôt que
//! de les dupliquer garde une seule définition du RBAC.

use sqlx::PgPool;
use uuid::Uuid;

use crate::error::{AppError, AppResult};
use crate::models::HouseRole;

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

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn role_sets_match_the_csharp_call_sites() {
        assert_eq!(WRITERS, &[HouseRole::Owner, HouseRole::CollaboratorRW]);
        assert_eq!(ANY_ROLE.len(), 4);
        assert!(ANY_ROLE.contains(&HouseRole::Tenant));
    }
}
