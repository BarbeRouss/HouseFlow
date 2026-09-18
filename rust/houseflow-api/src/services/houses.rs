//! Maisons — portage de `HouseService`.

use serde_json::Value;
use sqlx::PgPool;
use uuid::Uuid;

use crate::audit::{self, AuditContext};
use crate::clock;
use crate::dto::devices::DeviceSummaryDto;
use crate::dto::houses::{
    CreateHouseRequest, HouseDetailDto, HouseDto, HouseSummaryDto, HousesListResponse,
    UpdateHouseRequest,
};
use crate::error::{AppError, AppResult};
use crate::models::{House, HouseRole};
use crate::services::calculator::{self, round_half_to_even};
use crate::services::devices;
use crate::services::members;
use crate::services::snapshots::{self, HouseSnapshot};

/// `GetUserHousesAsync` : maisons possédées **ou** partagées, avec leurs scores.
pub async fn get_user_houses(pool: &PgPool, user_id: Uuid) -> AppResult<HousesListResponse> {
    let house_ids = snapshots::accessible_house_ids(pool, user_id).await?;
    let houses = snapshots::load_houses(pool, &house_ids).await?;

    let mut summaries = Vec::with_capacity(houses.len());
    for snapshot in &houses {
        // Une maison sans rôle résolu est ignorée, comme le `Where(userRole.ContainsKey)` du C#.
        let Some(role) = members::get_user_role(pool, snapshot.house.id, user_id).await? else {
            continue;
        };
        summaries.push(house_summary(snapshot, role)?);
    }

    // `Math.Round(houseSummaries.Average(h => h.Score))`, 100 s'il n'y a aucune maison.
    let global_score = if summaries.is_empty() {
        100
    } else {
        let total: i64 = summaries
            .iter()
            .map(|summary| i64::from(summary.score))
            .sum();
        round_half_to_even(total as f64 / summaries.len() as f64)
    };

    Ok(HousesListResponse {
        houses: summaries,
        global_score,
    })
}

/// `GetHouseDetailAsync` : `None` si l'utilisateur n'a aucun rôle sur la maison ou si
/// elle n'existe pas — le contrôleur en fait un 404 sans corps.
pub async fn get_house_detail(
    pool: &PgPool,
    house_id: Uuid,
    user_id: Uuid,
) -> AppResult<Option<HouseDetailDto>> {
    let Some(role) = members::get_user_role(pool, house_id, user_id).await? else {
        return Ok(None);
    };
    let Some(snapshot) = snapshots::load_house(pool, house_id).await? else {
        return Ok(None);
    };

    let devices: Vec<DeviceSummaryDto> = snapshot
        .devices
        .iter()
        .map(devices::device_summary)
        .collect::<AppResult<_>>()?;
    let score = calculator::calculate_house_score(&snapshot.all_maintenance_types())
        .map_err(|error| AppError::BadRequest(error.to_string()))?;

    Ok(Some(HouseDetailDto {
        id: snapshot.house.id,
        name: snapshot.house.name.clone(),
        address: snapshot.house.address.clone(),
        zip_code: snapshot.house.zip_code.clone(),
        city: snapshot.house.city.clone(),
        created_at: snapshot.house.created_at,
        score: score.score,
        devices_count: snapshot.devices.len() as i32,
        pending_count: score.pending_count,
        overdue_count: score.overdue_count,
        devices,
        user_role: Some(role.to_string()),
    }))
}

/// `CreateHouseAsync` : la maison et l'adhésion `Owner` sont créées ensemble.
pub async fn create_house(
    pool: &PgPool,
    context: &AuditContext,
    user_id: Uuid,
    request: &CreateHouseRequest,
) -> AppResult<HouseDto> {
    let now = clock::now();
    let house = House {
        id: Uuid::new_v4(),
        name: request.name().to_string(),
        address: request.address.clone(),
        zip_code: request.zip_code.clone(),
        city: request.city.clone(),
        // `CreateHouseRequest` ne porte pas de pays : la colonne reste nulle.
        country: None,
        created_at: now,
        updated_at: None,
        user_id,
    };

    let mut tx = pool.begin().await?;

    sqlx::query(
        r#"INSERT INTO "Houses" ("Id", "Name", "Address", "ZipCode", "City", "CreatedAt", "UserId")
           VALUES ($1, $2, $3, $4, $5, $6, $7)"#,
    )
    .bind(house.id)
    .bind(&house.name)
    .bind(house.address.as_deref())
    .bind(house.zip_code.as_deref())
    .bind(house.city.as_deref())
    .bind(house.created_at)
    .bind(house.user_id)
    .execute(&mut *tx)
    .await?;

    audit::record_added(&mut tx, context, "House", house.id, &house_values(&house)).await?;

    let member_id = Uuid::new_v4();
    sqlx::query(
        r#"INSERT INTO "HouseMembers"
               ("Id", "Role", "CanLogMaintenance", "CreatedAt", "UserId", "HouseId", "CanViewCosts")
           VALUES ($1, $2, true, $3, $4, $5, false)"#,
    )
    .bind(member_id)
    .bind(HouseRole::Owner.to_string())
    .bind(now)
    .bind(user_id)
    .bind(house.id)
    .execute(&mut *tx)
    .await?;

    audit::record_added(
        &mut tx,
        context,
        "HouseMember",
        member_id,
        &audit::values([
            ("Id", audit::id(member_id)),
            ("Role", Value::String(HouseRole::Owner.to_string())),
            ("CanLogMaintenance", Value::Bool(true)),
            ("CanViewCosts", Value::Bool(false)),
            ("CreatedAt", audit::date(now)),
            ("UpdatedAt", Value::Null),
            ("UserId", audit::id(user_id)),
            ("HouseId", audit::id(house.id)),
        ]),
    )
    .await?;

    tx.commit().await?;

    Ok(HouseDto::from(&house))
}

/// `UpdateHouseAsync` : `None` si la maison n'existe pas (404 sans corps), 403 si
/// l'utilisateur n'en est pas propriétaire — l'ordre des deux contrôles est celui du C#.
pub async fn update_house(
    pool: &PgPool,
    context: &AuditContext,
    house_id: Uuid,
    user_id: Uuid,
    request: &UpdateHouseRequest,
) -> AppResult<Option<HouseDto>> {
    let Some(house) = find_house(pool, house_id).await? else {
        return Ok(None);
    };
    members::ensure_owner(pool, house_id, user_id).await?;

    let mut updated = house.clone();
    if let Some(name) = request.name.clone() {
        updated.name = name;
    }
    if request.address.is_some() {
        updated.address = request.address.clone();
    }
    if request.zip_code.is_some() {
        updated.zip_code = request.zip_code.clone();
    }
    if request.city.is_some() {
        updated.city = request.city.clone();
    }
    updated.updated_at = Some(clock::now());

    let mut tx = pool.begin().await?;

    sqlx::query(
        r#"UPDATE "Houses"
              SET "Name" = $1, "Address" = $2, "ZipCode" = $3, "City" = $4, "UpdatedAt" = $5
            WHERE "Id" = $6"#,
    )
    .bind(&updated.name)
    .bind(updated.address.as_deref())
    .bind(updated.zip_code.as_deref())
    .bind(updated.city.as_deref())
    .bind(updated.updated_at)
    .bind(house_id)
    .execute(&mut *tx)
    .await?;

    let before = house_values(&house);
    let after = house_values(&updated);
    let (old_values, new_values, changed) = audit::diff(&before, &after);

    audit::record_modified(
        &mut tx,
        context,
        "House",
        house_id,
        &old_values,
        &new_values,
        &changed,
    )
    .await?;

    tx.commit().await?;

    Ok(Some(HouseDto::from(&updated)))
}

/// `DeleteHouseAsync` : réservé au propriétaire. Les appareils, entretiens, adhésions
/// et invitations suivent par cascade (`ON DELETE CASCADE`, comme EF).
pub async fn delete_house(
    pool: &PgPool,
    context: &AuditContext,
    house_id: Uuid,
    user_id: Uuid,
) -> AppResult<bool> {
    let Some(house) = find_house(pool, house_id).await? else {
        return Ok(false);
    };
    members::ensure_owner(pool, house_id, user_id).await?;

    let mut tx = pool.begin().await?;

    sqlx::query(r#"DELETE FROM "Houses" WHERE "Id" = $1"#)
        .bind(house_id)
        .execute(&mut *tx)
        .await?;

    audit::record_deleted(&mut tx, context, "House", house_id, &house_values(&house)).await?;

    tx.commit().await?;
    Ok(true)
}

/// Valeurs auditées d'une maison, dans l'ordre des propriétés de `House.cs`.
fn house_values(house: &House) -> audit::Values {
    audit::values([
        ("Id", audit::id(house.id)),
        ("Name", Value::String(house.name.clone())),
        ("Address", audit::opt_str(house.address.as_deref())),
        ("ZipCode", audit::opt_str(house.zip_code.as_deref())),
        ("City", audit::opt_str(house.city.as_deref())),
        ("Country", audit::opt_str(house.country.as_deref())),
        ("CreatedAt", audit::date(house.created_at)),
        ("UpdatedAt", audit::opt_date(house.updated_at)),
        ("UserId", audit::id(house.user_id)),
    ])
}

async fn find_house(pool: &PgPool, house_id: Uuid) -> AppResult<Option<House>> {
    Ok(sqlx::query_as(r#"SELECT * FROM "Houses" WHERE "Id" = $1"#)
        .bind(house_id)
        .fetch_optional(pool)
        .await?)
}

/// `CalculateHouseSummary`.
fn house_summary(snapshot: &HouseSnapshot, role: HouseRole) -> AppResult<HouseSummaryDto> {
    let score = calculator::calculate_house_score(&snapshot.all_maintenance_types())
        .map_err(|error| AppError::BadRequest(error.to_string()))?;

    Ok(HouseSummaryDto {
        id: snapshot.house.id,
        name: snapshot.house.name.clone(),
        address: snapshot.house.address.clone(),
        zip_code: snapshot.house.zip_code.clone(),
        city: snapshot.house.city.clone(),
        created_at: snapshot.house.created_at,
        score: score.score,
        devices_count: snapshot.devices.len() as i32,
        pending_count: score.pending_count,
        overdue_count: score.overdue_count,
        user_role: Some(role.to_string()),
    })
}
