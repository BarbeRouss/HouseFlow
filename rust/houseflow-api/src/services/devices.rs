//! Appareils — portage de `DeviceService`.

use rust_decimal::Decimal;
use serde_json::Value;
use sqlx::PgPool;
use uuid::Uuid;

use crate::audit::{self, AuditContext};
use crate::clock;
use crate::dto::devices::{
    CreateDeviceRequest, DeviceDetailDto, DeviceDto, DeviceSummaryDto, UpdateDeviceRequest,
};
use crate::dto::maintenance::MaintenanceTypeWithStatusDto;
use crate::error::{AppError, AppResult};
use crate::models::Device;
use crate::services::calculator;
use crate::services::members;
use crate::services::snapshots::{self, DeviceSnapshot};

/// `GetHouseDevicesAsync` : tout membre de la maison peut lister ses appareils.
pub async fn get_house_devices(
    pool: &PgPool,
    house_id: Uuid,
    user_id: Uuid,
) -> AppResult<Vec<DeviceSummaryDto>> {
    members::ensure_member(pool, house_id, user_id).await?;

    snapshots::load_house_devices(pool, house_id)
        .await?
        .iter()
        .map(device_summary)
        .collect()
}

/// `GetDeviceDetailAsync` : `None` si l'appareil n'existe pas (404 sans corps), 403 si
/// l'utilisateur n'est pas membre de sa maison.
pub async fn get_device_detail(
    pool: &PgPool,
    device_id: Uuid,
    user_id: Uuid,
) -> AppResult<Option<DeviceDetailDto>> {
    let Some(snapshot) = snapshots::load_device(pool, device_id).await? else {
        return Ok(None);
    };
    members::ensure_member(pool, snapshot.device.house_id, user_id).await?;

    // H1 : les coûts sont masqués aux locataires sans permission.
    let hide_costs = members::should_hide_costs(pool, snapshot.device.house_id, user_id).await?;

    let type_ids: Vec<Uuid> = snapshot
        .maintenance_types
        .iter()
        .map(|value| value.id)
        .collect();
    let instances = snapshots::load_instances(pool, &type_ids).await?;

    let total_spent = if hide_costs {
        Decimal::ZERO
    } else {
        instances
            .iter()
            .map(|instance| instance.cost.unwrap_or(Decimal::ZERO))
            .sum()
    };

    let score = calculator::calculate_device_score(&snapshot.maintenance_types)
        .map_err(|error| AppError::BadRequest(error.to_string()))?;
    let maintenance_types: Vec<MaintenanceTypeWithStatusDto> = snapshot
        .maintenance_types
        .iter()
        .map(|value| {
            calculator::calculate_maintenance_type_with_status(value)
                .map(MaintenanceTypeWithStatusDto::from)
                .map_err(|error| AppError::BadRequest(error.to_string()))
        })
        .collect::<AppResult<_>>()?;

    Ok(Some(DeviceDetailDto {
        id: snapshot.device.id,
        name: snapshot.device.name.clone(),
        device_type: snapshot.device.device_type.clone(),
        brand: snapshot.device.brand.clone(),
        model: snapshot.device.model.clone(),
        install_date: snapshot.device.install_date,
        house_id: snapshot.device.house_id,
        created_at: snapshot.device.created_at,
        score: score.score,
        status: score.status.to_string(),
        pending_count: score.pending_count,
        maintenance_types_count: snapshot.maintenance_types.len() as i32,
        maintenance_types,
        total_spent,
        maintenance_count: instances.len() as i32,
    }))
}

/// `CreateDeviceAsync` : propriétaire et collaborateur en écriture seulement.
pub async fn create_device(
    pool: &PgPool,
    context: &AuditContext,
    house_id: Uuid,
    user_id: Uuid,
    request: &CreateDeviceRequest,
) -> AppResult<DeviceDto> {
    members::ensure_writer(pool, house_id, user_id).await?;

    let device = Device {
        id: Uuid::new_v4(),
        name: request.name().to_string(),
        device_type: request.device_type().to_string(),
        install_date: request.install_date,
        created_at: clock::now(),
        updated_at: None,
        house_id,
        brand: request.brand.clone(),
        model: request.model.clone(),
    };

    let mut tx = pool.begin().await?;

    sqlx::query(
        r#"INSERT INTO "Devices"
               ("Id", "Name", "Type", "Brand", "Model", "InstallDate", "CreatedAt", "HouseId")
           VALUES ($1, $2, $3, $4, $5, $6, $7, $8)"#,
    )
    .bind(device.id)
    .bind(&device.name)
    .bind(&device.device_type)
    .bind(device.brand.as_deref())
    .bind(device.model.as_deref())
    .bind(device.install_date)
    .bind(device.created_at)
    .bind(device.house_id)
    .execute(&mut *tx)
    .await?;

    audit::record_added(
        &mut tx,
        context,
        "Device",
        device.id,
        &device_values(&device),
    )
    .await?;

    tx.commit().await?;

    Ok(DeviceDto::from(&device))
}

/// `UpdateDeviceAsync` : `None` si l'appareil n'existe pas, 403 si l'utilisateur n'a
/// pas le droit d'écrire dans sa maison.
pub async fn update_device(
    pool: &PgPool,
    context: &AuditContext,
    device_id: Uuid,
    user_id: Uuid,
    request: &UpdateDeviceRequest,
) -> AppResult<Option<DeviceDto>> {
    let Some(device) = find_device(pool, device_id).await? else {
        return Ok(None);
    };
    members::ensure_writer(pool, device.house_id, user_id).await?;

    let mut updated = device.clone();
    if let Some(name) = request.name.clone() {
        updated.name = name;
    }
    if let Some(device_type) = request.device_type.clone() {
        updated.device_type = device_type;
    }
    if request.brand.is_some() {
        updated.brand = request.brand.clone();
    }
    if request.model.is_some() {
        updated.model = request.model.clone();
    }
    if request.install_date.is_some() {
        updated.install_date = request.install_date;
    }
    updated.updated_at = Some(clock::now());

    let mut tx = pool.begin().await?;

    sqlx::query(
        r#"UPDATE "Devices"
              SET "Name" = $1, "Type" = $2, "Brand" = $3, "Model" = $4, "InstallDate" = $5,
                  "UpdatedAt" = $6
            WHERE "Id" = $7"#,
    )
    .bind(&updated.name)
    .bind(&updated.device_type)
    .bind(updated.brand.as_deref())
    .bind(updated.model.as_deref())
    .bind(updated.install_date)
    .bind(updated.updated_at)
    .bind(device_id)
    .execute(&mut *tx)
    .await?;

    let before = device_values(&device);
    let after = device_values(&updated);
    let (old_values, new_values, changed) = audit::diff(&before, &after);
    audit::record_modified(
        &mut tx,
        context,
        "Device",
        device_id,
        &old_values,
        &new_values,
        &changed,
    )
    .await?;

    tx.commit().await?;

    Ok(Some(DeviceDto::from(&updated)))
}

/// `DeleteDeviceAsync` : les types d'entretien et leurs interventions suivent par cascade.
pub async fn delete_device(
    pool: &PgPool,
    context: &AuditContext,
    device_id: Uuid,
    user_id: Uuid,
) -> AppResult<bool> {
    let Some(device) = find_device(pool, device_id).await? else {
        return Ok(false);
    };
    members::ensure_writer(pool, device.house_id, user_id).await?;

    let mut tx = pool.begin().await?;

    sqlx::query(r#"DELETE FROM "Devices" WHERE "Id" = $1"#)
        .bind(device_id)
        .execute(&mut *tx)
        .await?;

    audit::record_deleted(
        &mut tx,
        context,
        "Device",
        device_id,
        &device_values(&device),
    )
    .await?;

    tx.commit().await?;
    Ok(true)
}

/// `CalculateDeviceSummary`, partagé avec `HouseService`.
pub fn device_summary(snapshot: &DeviceSnapshot) -> AppResult<DeviceSummaryDto> {
    let score = calculator::calculate_device_score(&snapshot.maintenance_types)
        .map_err(|error| AppError::BadRequest(error.to_string()))?;

    Ok(DeviceSummaryDto {
        id: snapshot.device.id,
        name: snapshot.device.name.clone(),
        device_type: snapshot.device.device_type.clone(),
        brand: snapshot.device.brand.clone(),
        model: snapshot.device.model.clone(),
        install_date: snapshot.device.install_date,
        house_id: snapshot.device.house_id,
        created_at: snapshot.device.created_at,
        score: score.score,
        status: score.status.to_string(),
        pending_count: score.pending_count,
        maintenance_types_count: snapshot.maintenance_types.len() as i32,
    })
}

async fn find_device(pool: &PgPool, device_id: Uuid) -> AppResult<Option<Device>> {
    Ok(sqlx::query_as(r#"SELECT * FROM "Devices" WHERE "Id" = $1"#)
        .bind(device_id)
        .fetch_optional(pool)
        .await?)
}

/// Valeurs auditées d'un appareil, dans l'ordre des propriétés de `Device.cs`.
fn device_values(device: &Device) -> audit::Values {
    audit::values([
        ("Id", audit::id(device.id)),
        ("Name", Value::String(device.name.clone())),
        ("Type", Value::String(device.device_type.clone())),
        ("Brand", audit::opt_str(device.brand.as_deref())),
        ("Model", audit::opt_str(device.model.as_deref())),
        ("InstallDate", audit::opt_date(device.install_date)),
        ("CreatedAt", audit::date(device.created_at)),
        ("UpdatedAt", audit::opt_date(device.updated_at)),
        ("HouseId", audit::id(device.house_id)),
    ])
}
