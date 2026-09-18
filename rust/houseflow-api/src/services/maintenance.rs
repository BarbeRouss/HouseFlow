//! Types d'entretien, interventions, historique et tâches à venir — portage de
//! `MaintenanceService`.

use chrono::{DateTime, Utc};
use rust_decimal::Decimal;
use serde_json::Value;
use sqlx::PgPool;
use uuid::Uuid;

use crate::audit::{self, AuditContext};
use crate::clock;
use crate::dto::maintenance::{
    CreateMaintenanceTypeRequest, LogMaintenanceRequest, MaintenanceHistoryResponse,
    MaintenanceInstanceDto, MaintenanceTypeDto, MaintenanceTypeWithStatusDto, UpcomingTaskDto,
    UpcomingTasksResponse, UpdateMaintenanceInstanceRequest, UpdateMaintenanceTypeRequest,
};
use crate::error::{AppError, AppResult};
use crate::models::{Device, MaintenanceInstance, MaintenanceType, Periodicity};
use crate::services::calculator::{self, STATUS_OVERDUE, STATUS_PENDING};
use crate::services::members;
use crate::services::snapshots;

/// Message de l'`InvalidOperationException` levée quand `Custom` n'a pas de durée.
const CUSTOM_DAYS_REQUIRED: &str = "CustomDays is required when periodicity is Custom.";
/// Message de l'`InvalidOperationException` levée pour une date d'entretien future.
const DATE_IN_FUTURE: &str = "Maintenance date cannot be in the future";

/// `GetDeviceMaintenanceTypesAsync` : 404 si l'appareil n'existe pas, 403 si
/// l'utilisateur n'est pas membre de sa maison.
pub async fn get_device_maintenance_types(
    pool: &PgPool,
    device_id: Uuid,
    user_id: Uuid,
) -> AppResult<Vec<MaintenanceTypeWithStatusDto>> {
    let snapshot = snapshots::load_device(pool, device_id)
        .await?
        .ok_or_else(|| AppError::NotFound("Device not found".to_string()))?;

    members::ensure_member(pool, snapshot.device.house_id, user_id).await?;

    snapshot
        .maintenance_types
        .iter()
        .map(|value| {
            calculator::calculate_maintenance_type_with_status(value)
                .map(MaintenanceTypeWithStatusDto::from)
                .map_err(|error| AppError::BadRequest(error.to_string()))
        })
        .collect()
}

/// `CreateMaintenanceTypeAsync`.
pub async fn create_maintenance_type(
    pool: &PgPool,
    context: &AuditContext,
    device_id: Uuid,
    user_id: Uuid,
    request: &CreateMaintenanceTypeRequest,
) -> AppResult<MaintenanceTypeDto> {
    let device = find_device(pool, device_id)
        .await?
        .ok_or_else(|| AppError::NotFound("Device not found".to_string()))?;

    members::ensure_writer(pool, device.house_id, user_id).await?;

    let periodicity = request.periodicity();
    if periodicity == Periodicity::Custom && request.custom_days.is_none() {
        return Err(AppError::BadRequest(CUSTOM_DAYS_REQUIRED.to_string()));
    }

    let maintenance_type = MaintenanceType {
        id: Uuid::new_v4(),
        name: request.name().to_string(),
        periodicity,
        custom_days: request.custom_days,
        created_at: clock::now(),
        updated_at: None,
        device_id,
    };

    let mut tx = pool.begin().await?;

    sqlx::query(
        r#"INSERT INTO "MaintenanceTypes"
               ("Id", "Name", "Periodicity", "CustomDays", "CreatedAt", "DeviceId")
           VALUES ($1, $2, $3, $4, $5, $6)"#,
    )
    .bind(maintenance_type.id)
    .bind(&maintenance_type.name)
    .bind(maintenance_type.periodicity.as_i32())
    .bind(maintenance_type.custom_days)
    .bind(maintenance_type.created_at)
    .bind(maintenance_type.device_id)
    .execute(&mut *tx)
    .await?;

    audit::record_added(
        &mut tx,
        context,
        "MaintenanceType",
        maintenance_type.id,
        &type_values(&maintenance_type),
    )
    .await?;

    tx.commit().await?;

    Ok(type_dto(&maintenance_type))
}

/// `UpdateMaintenanceTypeAsync` : `None` si le type n'existe pas (404 sans corps).
pub async fn update_maintenance_type(
    pool: &PgPool,
    context: &AuditContext,
    type_id: Uuid,
    user_id: Uuid,
    request: &UpdateMaintenanceTypeRequest,
) -> AppResult<Option<MaintenanceTypeDto>> {
    let Some((maintenance_type, device)) = find_type_with_device(pool, type_id).await? else {
        return Ok(None);
    };
    members::ensure_writer(pool, device.house_id, user_id).await?;

    // Le C# valide la combinaison *effective* : périodicité et durée peuvent venir
    // l'une de la requête, l'autre de l'existant.
    let effective_periodicity = request.periodicity.unwrap_or(maintenance_type.periodicity);
    let effective_custom_days = request.custom_days.or(maintenance_type.custom_days);
    if effective_periodicity == Periodicity::Custom && effective_custom_days.is_none() {
        return Err(AppError::BadRequest(CUSTOM_DAYS_REQUIRED.to_string()));
    }

    let mut updated = maintenance_type.clone();
    if let Some(name) = request.name.clone() {
        updated.name = name;
    }
    if let Some(periodicity) = request.periodicity {
        updated.periodicity = periodicity;
    }
    if request.custom_days.is_some() {
        updated.custom_days = request.custom_days;
    }
    updated.updated_at = Some(clock::now());

    let mut tx = pool.begin().await?;

    sqlx::query(
        r#"UPDATE "MaintenanceTypes"
              SET "Name" = $1, "Periodicity" = $2, "CustomDays" = $3, "UpdatedAt" = $4
            WHERE "Id" = $5"#,
    )
    .bind(&updated.name)
    .bind(updated.periodicity.as_i32())
    .bind(updated.custom_days)
    .bind(updated.updated_at)
    .bind(type_id)
    .execute(&mut *tx)
    .await?;

    let before = type_values(&maintenance_type);
    let after = type_values(&updated);
    let (old_values, new_values, changed) = audit::diff(&before, &after);
    audit::record_modified(
        &mut tx,
        context,
        "MaintenanceType",
        type_id,
        &old_values,
        &new_values,
        &changed,
    )
    .await?;

    tx.commit().await?;

    Ok(Some(type_dto(&updated)))
}

/// `DeleteMaintenanceTypeAsync` : les interventions suivent par cascade.
pub async fn delete_maintenance_type(
    pool: &PgPool,
    context: &AuditContext,
    type_id: Uuid,
    user_id: Uuid,
) -> AppResult<bool> {
    let Some((maintenance_type, device)) = find_type_with_device(pool, type_id).await? else {
        return Ok(false);
    };
    members::ensure_writer(pool, device.house_id, user_id).await?;

    let mut tx = pool.begin().await?;

    sqlx::query(r#"DELETE FROM "MaintenanceTypes" WHERE "Id" = $1"#)
        .bind(type_id)
        .execute(&mut *tx)
        .await?;

    audit::record_deleted(
        &mut tx,
        context,
        "MaintenanceType",
        type_id,
        &type_values(&maintenance_type),
    )
    .await?;

    tx.commit().await?;
    Ok(true)
}

/// `LogMaintenanceAsync` : propriétaire, collaborateur RW, ou locataire autorisé.
pub async fn log_maintenance(
    pool: &PgPool,
    context: &AuditContext,
    type_id: Uuid,
    user_id: Uuid,
    request: &LogMaintenanceRequest,
) -> AppResult<MaintenanceInstanceDto> {
    let (maintenance_type, device) = find_type_with_device(pool, type_id)
        .await?
        .ok_or_else(|| AppError::NotFound("Maintenance type not found".to_string()))?;

    members::ensure_can_log_maintenance(pool, device.house_id, user_id).await?;

    // `[Required]` garantit la présence de la date ; la garde reste pour le service.
    let date = request
        .date
        .ok_or_else(|| AppError::BadRequest("The Date field is required.".to_string()))?;
    if date > Utc::now() {
        return Err(AppError::BadRequest(DATE_IN_FUTURE.to_string()));
    }

    let instance = MaintenanceInstance {
        id: Uuid::new_v4(),
        date,
        cost: request.cost,
        provider: request.provider.clone(),
        notes: request.notes.clone(),
        created_at: clock::now(),
        updated_at: None,
        maintenance_type_id: type_id,
    };

    let mut tx = pool.begin().await?;

    sqlx::query(
        r#"INSERT INTO "MaintenanceInstances"
               ("Id", "Date", "Cost", "Provider", "Notes", "CreatedAt", "MaintenanceTypeId")
           VALUES ($1, $2, $3, $4, $5, $6, $7)"#,
    )
    .bind(instance.id)
    .bind(instance.date)
    .bind(instance.cost)
    .bind(instance.provider.as_deref())
    .bind(instance.notes.as_deref())
    .bind(instance.created_at)
    .bind(instance.maintenance_type_id)
    .execute(&mut *tx)
    .await?;

    audit::record_added(
        &mut tx,
        context,
        "MaintenanceInstance",
        instance.id,
        &instance_values(&instance),
    )
    .await?;

    tx.commit().await?;

    Ok(instance_dto(&instance, &maintenance_type.name))
}

/// `GetDeviceMaintenanceHistoryAsync` : interventions de tous les types de l'appareil,
/// de la plus récente à la plus ancienne.
pub async fn get_device_maintenance_history(
    pool: &PgPool,
    device_id: Uuid,
    user_id: Uuid,
) -> AppResult<MaintenanceHistoryResponse> {
    let snapshot = snapshots::load_device(pool, device_id)
        .await?
        .ok_or_else(|| AppError::NotFound("Device not found".to_string()))?;

    members::ensure_member(pool, snapshot.device.house_id, user_id).await?;

    // Coûts et prestataire masqués aux locataires sans permission.
    let hide_costs = members::should_hide_costs(pool, snapshot.device.house_id, user_id).await?;

    let type_ids: Vec<Uuid> = snapshot
        .maintenance_types
        .iter()
        .map(|value| value.id)
        .collect();
    let names: std::collections::HashMap<Uuid, &str> = snapshot
        .maintenance_types
        .iter()
        .map(|value| (value.id, value.name.as_str()))
        .collect();

    let mut instances: Vec<MaintenanceInstanceDto> = snapshots::load_instances(pool, &type_ids)
        .await?
        .iter()
        .map(|instance| {
            let name = names
                .get(&instance.maintenance_type_id)
                .copied()
                .unwrap_or_default();
            let mut dto = instance_dto(instance, name);
            if hide_costs {
                dto.cost = None;
                dto.provider = None;
            }
            dto
        })
        .collect();

    // `OrderByDescending(i => i.Date)` : tri stable, comme LINQ.
    instances.sort_by(|left, right| right.date.cmp(&left.date));

    let total_spent = if hide_costs {
        Decimal::ZERO
    } else {
        instances
            .iter()
            .map(|instance| instance.cost.unwrap_or(Decimal::ZERO))
            .sum()
    };

    Ok(MaintenanceHistoryResponse {
        count: instances.len() as i32,
        instances,
        total_spent,
    })
}

/// `UpdateMaintenanceInstanceAsync`.
pub async fn update_maintenance_instance(
    pool: &PgPool,
    context: &AuditContext,
    instance_id: Uuid,
    user_id: Uuid,
    request: &UpdateMaintenanceInstanceRequest,
) -> AppResult<Option<MaintenanceInstanceDto>> {
    let Some((instance, maintenance_type, device)) =
        find_instance_with_parents(pool, instance_id).await?
    else {
        return Ok(None);
    };
    members::ensure_writer(pool, device.house_id, user_id).await?;

    let mut updated = instance.clone();
    if let Some(date) = request.date {
        if date > Utc::now() {
            return Err(AppError::BadRequest(DATE_IN_FUTURE.to_string()));
        }
        updated.date = date;
    }
    if request.cost.is_some() {
        updated.cost = request.cost;
    }
    if request.provider.is_some() {
        updated.provider = request.provider.clone();
    }
    if request.notes.is_some() {
        updated.notes = request.notes.clone();
    }
    updated.updated_at = Some(clock::now());

    let mut tx = pool.begin().await?;

    sqlx::query(
        r#"UPDATE "MaintenanceInstances"
              SET "Date" = $1, "Cost" = $2, "Provider" = $3, "Notes" = $4, "UpdatedAt" = $5
            WHERE "Id" = $6"#,
    )
    .bind(updated.date)
    .bind(updated.cost)
    .bind(updated.provider.as_deref())
    .bind(updated.notes.as_deref())
    .bind(updated.updated_at)
    .bind(instance_id)
    .execute(&mut *tx)
    .await?;

    let before = instance_values(&instance);
    let after = instance_values(&updated);
    let (old_values, new_values, changed) = audit::diff(&before, &after);
    audit::record_modified(
        &mut tx,
        context,
        "MaintenanceInstance",
        instance_id,
        &old_values,
        &new_values,
        &changed,
    )
    .await?;

    tx.commit().await?;

    Ok(Some(instance_dto(&updated, &maintenance_type.name)))
}

/// `DeleteMaintenanceInstanceAsync`.
pub async fn delete_maintenance_instance(
    pool: &PgPool,
    context: &AuditContext,
    instance_id: Uuid,
    user_id: Uuid,
) -> AppResult<bool> {
    let Some((instance, _, device)) = find_instance_with_parents(pool, instance_id).await? else {
        return Ok(false);
    };
    members::ensure_writer(pool, device.house_id, user_id).await?;

    let mut tx = pool.begin().await?;

    sqlx::query(r#"DELETE FROM "MaintenanceInstances" WHERE "Id" = $1"#)
        .bind(instance_id)
        .execute(&mut *tx)
        .await?;

    audit::record_deleted(
        &mut tx,
        context,
        "MaintenanceInstance",
        instance_id,
        &instance_values(&instance),
    )
    .await?;

    tx.commit().await?;
    Ok(true)
}

/// `GetUpcomingTasksAsync` : tâches `pending` ou `overdue` de toutes les maisons
/// accessibles. Les compteurs portent sur l'ensemble, `limit` ne coupe que la liste.
pub async fn get_upcoming_tasks(
    pool: &PgPool,
    user_id: Uuid,
    limit: Option<i32>,
) -> AppResult<UpcomingTasksResponse> {
    let house_ids = snapshots::accessible_house_ids(pool, user_id).await?;
    let houses = snapshots::load_houses(pool, &house_ids).await?;

    let mut tasks: Vec<UpcomingTaskDto> = Vec::new();
    for house in &houses {
        for device in &house.devices {
            for maintenance_type in &device.maintenance_types {
                let with_status =
                    calculator::calculate_maintenance_type_with_status(maintenance_type)
                        .map_err(|error| AppError::BadRequest(error.to_string()))?;

                if with_status.status != STATUS_PENDING && with_status.status != STATUS_OVERDUE {
                    continue;
                }

                tasks.push(UpcomingTaskDto {
                    maintenance_type_id: maintenance_type.id,
                    maintenance_type_name: maintenance_type.name.clone(),
                    device_id: device.device.id,
                    device_name: device.device.name.clone(),
                    device_type: device.device.device_type.clone(),
                    house_id: house.house.id,
                    house_name: house.house.name.clone(),
                    status: with_status.status.to_string(),
                    next_due_date: with_status.next_due_date,
                    last_maintenance_date: with_status.last_maintenance_date,
                    periodicity: maintenance_type.periodicity.to_string(),
                });
            }
        }
    }

    sort_upcoming_tasks(&mut tasks);

    let overdue_count = tasks
        .iter()
        .filter(|task| task.status == STATUS_OVERDUE)
        .count() as i32;
    let pending_count = tasks
        .iter()
        .filter(|task| task.status == STATUS_PENDING)
        .count() as i32;

    // `Take(limit)` : une limite négative rend une liste vide, comme LINQ.
    if let Some(limit) = limit {
        tasks.truncate(limit.max(0) as usize);
    }

    Ok(UpcomingTasksResponse {
        tasks,
        overdue_count,
        pending_count,
    })
}

/// Tri du C# : jamais faites d'abord, puis les retards, puis par échéance croissante.
///
/// `sort_by` est stable, comme les `OrderBy`/`ThenBy` de LINQ : à clés égales,
/// l'ordre de construction (maison, appareil, type) est conservé.
fn sort_upcoming_tasks(tasks: &mut [UpcomingTaskDto]) {
    tasks.sort_by_key(upcoming_sort_key);
}

fn upcoming_sort_key(task: &UpcomingTaskDto) -> (u8, u8, DateTime<Utc>) {
    (
        u8::from(task.next_due_date.is_some()),
        u8::from(task.status != STATUS_OVERDUE),
        // `t.NextDueDate ?? DateTime.MaxValue`.
        task.next_due_date.unwrap_or(DateTime::<Utc>::MAX_UTC),
    )
}

async fn find_device(pool: &PgPool, device_id: Uuid) -> AppResult<Option<Device>> {
    Ok(sqlx::query_as(r#"SELECT * FROM "Devices" WHERE "Id" = $1"#)
        .bind(device_id)
        .fetch_optional(pool)
        .await?)
}

/// `Include(mt => mt.Device)` : le type et l'appareil qui le porte.
async fn find_type_with_device(
    pool: &PgPool,
    type_id: Uuid,
) -> AppResult<Option<(MaintenanceType, Device)>> {
    let maintenance_type: Option<MaintenanceType> =
        sqlx::query_as(r#"SELECT * FROM "MaintenanceTypes" WHERE "Id" = $1"#)
            .bind(type_id)
            .fetch_optional(pool)
            .await?;

    let Some(maintenance_type) = maintenance_type else {
        return Ok(None);
    };
    let Some(device) = find_device(pool, maintenance_type.device_id).await? else {
        return Ok(None);
    };

    Ok(Some((maintenance_type, device)))
}

/// `Include(i => i.MaintenanceType).ThenInclude(mt => mt.Device)`.
async fn find_instance_with_parents(
    pool: &PgPool,
    instance_id: Uuid,
) -> AppResult<Option<(MaintenanceInstance, MaintenanceType, Device)>> {
    let instance: Option<MaintenanceInstance> =
        sqlx::query_as(r#"SELECT * FROM "MaintenanceInstances" WHERE "Id" = $1"#)
            .bind(instance_id)
            .fetch_optional(pool)
            .await?;

    let Some(instance) = instance else {
        return Ok(None);
    };
    let Some((maintenance_type, device)) =
        find_type_with_device(pool, instance.maintenance_type_id).await?
    else {
        return Ok(None);
    };

    Ok(Some((instance, maintenance_type, device)))
}

fn type_dto(value: &MaintenanceType) -> MaintenanceTypeDto {
    MaintenanceTypeDto {
        id: value.id,
        name: value.name.clone(),
        periodicity: value.periodicity,
        custom_days: value.custom_days,
        device_id: value.device_id,
        created_at: value.created_at,
    }
}

fn instance_dto(instance: &MaintenanceInstance, type_name: &str) -> MaintenanceInstanceDto {
    MaintenanceInstanceDto {
        id: instance.id,
        date: instance.date,
        cost: instance.cost,
        provider: instance.provider.clone(),
        notes: instance.notes.clone(),
        maintenance_type_id: instance.maintenance_type_id,
        maintenance_type_name: type_name.to_string(),
        created_at: instance.created_at,
    }
}

/// Valeurs auditées, dans l'ordre des propriétés de `MaintenanceType.cs`.
/// `Periodicity` est consignée telle qu'EF la stocke : son ordinal.
fn type_values(value: &MaintenanceType) -> audit::Values {
    audit::values([
        ("Id", audit::id(value.id)),
        ("Name", Value::String(value.name.clone())),
        ("Periodicity", Value::from(value.periodicity.as_i32())),
        (
            "CustomDays",
            value.custom_days.map(Value::from).unwrap_or(Value::Null),
        ),
        ("CreatedAt", audit::date(value.created_at)),
        ("UpdatedAt", audit::opt_date(value.updated_at)),
        ("DeviceId", audit::id(value.device_id)),
    ])
}

/// Valeurs auditées, dans l'ordre des propriétés de `MaintenanceInstance.cs`.
fn instance_values(instance: &MaintenanceInstance) -> audit::Values {
    audit::values([
        ("Id", audit::id(instance.id)),
        ("Date", audit::date(instance.date)),
        (
            "Cost",
            instance
                .cost
                .and_then(|cost| serde_json::to_value(cost).ok())
                .unwrap_or(Value::Null),
        ),
        ("Provider", audit::opt_str(instance.provider.as_deref())),
        ("Notes", audit::opt_str(instance.notes.as_deref())),
        ("CreatedAt", audit::date(instance.created_at)),
        ("UpdatedAt", audit::opt_date(instance.updated_at)),
        ("MaintenanceTypeId", audit::id(instance.maintenance_type_id)),
    ])
}

#[cfg(test)]
mod tests {
    use super::*;

    fn task(name: &str, status: &str, next_due: Option<DateTime<Utc>>) -> UpcomingTaskDto {
        UpcomingTaskDto {
            maintenance_type_id: Uuid::new_v4(),
            maintenance_type_name: name.to_string(),
            device_id: Uuid::new_v4(),
            device_name: "Appareil".into(),
            device_type: "heating".into(),
            house_id: Uuid::new_v4(),
            house_name: "Maison".into(),
            status: status.to_string(),
            next_due_date: next_due,
            last_maintenance_date: None,
            periodicity: "Annual".into(),
        }
    }

    fn at(year: i32, month: u32, day: u32) -> DateTime<Utc> {
        chrono::TimeZone::with_ymd_and_hms(&Utc, year, month, day, 0, 0, 0).unwrap()
    }

    fn names(tasks: &[UpcomingTaskDto]) -> Vec<&str> {
        tasks
            .iter()
            .map(|task| task.maintenance_type_name.as_str())
            .collect()
    }

    #[test]
    fn tasks_never_done_come_first() {
        let mut tasks = vec![
            task("échéance proche", STATUS_PENDING, Some(at(2026, 5, 1))),
            task("jamais faite", STATUS_PENDING, None),
        ];
        sort_upcoming_tasks(&mut tasks);
        assert_eq!(names(&tasks), vec!["jamais faite", "échéance proche"]);
    }

    #[test]
    fn overdue_tasks_come_before_pending_ones_even_when_due_later() {
        let mut tasks = vec![
            task("à venir", STATUS_PENDING, Some(at(2026, 1, 1))),
            task("en retard", STATUS_OVERDUE, Some(at(2026, 6, 1))),
        ];
        sort_upcoming_tasks(&mut tasks);
        assert_eq!(names(&tasks), vec!["en retard", "à venir"]);
    }

    #[test]
    fn tasks_of_the_same_kind_are_ordered_by_due_date() {
        let mut tasks = vec![
            task("plus tard", STATUS_OVERDUE, Some(at(2026, 3, 1))),
            task("plus tôt", STATUS_OVERDUE, Some(at(2026, 1, 1))),
        ];
        sort_upcoming_tasks(&mut tasks);
        assert_eq!(names(&tasks), vec!["plus tôt", "plus tard"]);
    }

    #[test]
    fn the_sort_is_stable_for_equal_keys() {
        let mut tasks = vec![
            task("première", STATUS_PENDING, None),
            task("deuxième", STATUS_PENDING, None),
            task("troisième", STATUS_PENDING, None),
        ];
        sort_upcoming_tasks(&mut tasks);
        assert_eq!(names(&tasks), vec!["première", "deuxième", "troisième"]);
    }
}
