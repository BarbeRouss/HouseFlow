//! Chargement des arbres maison → appareils → types d'entretien → interventions.
//!
//! Côté .NET, `HouseService`, `DeviceService` et `MaintenanceService` obtiennent ces
//! données en une requête EF avec `Include(...).ThenInclude(...)`. Ici, chaque niveau
//! est chargé en une requête `= ANY($1)` puis recousu en mémoire : même résultat, un
//! nombre de requêtes constant, et une seule définition de l'ordre des listes.
//!
//! **Ordre** : EF ne pose aucun `ORDER BY`, PostgreSQL rend donc les lignes dans
//! l'ordre d'insertion. On l'explicite par `"CreatedAt", "Id"` pour que les listes
//! soient reproductibles (la suite d'intégration prend par exemple
//! `houses.First()` pour retrouver la maison créée à l'inscription).

use std::collections::HashMap;

use sqlx::PgPool;
use uuid::Uuid;

use crate::error::AppResult;
use crate::models::{Device, House, MaintenanceInstance, MaintenanceType};
use crate::services::calculator::MaintenanceTypeSnapshot;

/// Un appareil et ses types d'entretien, prêts pour le calculateur.
#[derive(Debug, Clone)]
pub struct DeviceSnapshot {
    pub device: Device,
    pub maintenance_types: Vec<MaintenanceTypeSnapshot>,
}

/// Une maison et ses appareils.
#[derive(Debug, Clone)]
pub struct HouseSnapshot {
    pub house: House,
    pub devices: Vec<DeviceSnapshot>,
}

impl HouseSnapshot {
    /// Tous les types d'entretien de la maison, tous appareils confondus
    /// (`house.Devices.SelectMany(d => d.MaintenanceTypes)`).
    pub fn all_maintenance_types(&self) -> Vec<MaintenanceTypeSnapshot> {
        self.devices
            .iter()
            .flat_map(|device| device.maintenance_types.iter().cloned())
            .collect()
    }
}

/// Identifiants des maisons auxquelles l'utilisateur a accès : celles qu'il possède
/// (`"Houses"."UserId"`) et celles où il est membre.
pub async fn accessible_house_ids(pool: &PgPool, user_id: Uuid) -> AppResult<Vec<Uuid>> {
    let rows: Vec<(Uuid,)> = sqlx::query_as(
        r#"SELECT "Id" FROM "Houses" WHERE "UserId" = $1
           UNION
           SELECT "HouseId" FROM "HouseMembers" WHERE "UserId" = $1"#,
    )
    .bind(user_id)
    .fetch_all(pool)
    .await?;

    Ok(rows.into_iter().map(|(id,)| id).collect())
}

/// Charge les maisons demandées avec tout leur contenu.
pub async fn load_houses(pool: &PgPool, house_ids: &[Uuid]) -> AppResult<Vec<HouseSnapshot>> {
    if house_ids.is_empty() {
        return Ok(Vec::new());
    }

    let houses: Vec<House> =
        sqlx::query_as(r#"SELECT * FROM "Houses" WHERE "Id" = ANY($1) ORDER BY "CreatedAt", "Id""#)
            .bind(house_ids)
            .fetch_all(pool)
            .await?;

    let mut devices_by_house = load_devices_by_house(pool, house_ids).await?;

    Ok(houses
        .into_iter()
        .map(|house| HouseSnapshot {
            devices: devices_by_house.remove(&house.id).unwrap_or_default(),
            house,
        })
        .collect())
}

/// Charge une maison et son contenu, `None` si elle n'existe pas.
pub async fn load_house(pool: &PgPool, house_id: Uuid) -> AppResult<Option<HouseSnapshot>> {
    Ok(load_houses(pool, &[house_id]).await?.into_iter().next())
}

/// Appareils d'une maison, avec leurs types d'entretien.
pub async fn load_house_devices(pool: &PgPool, house_id: Uuid) -> AppResult<Vec<DeviceSnapshot>> {
    Ok(load_devices_by_house(pool, &[house_id])
        .await?
        .remove(&house_id)
        .unwrap_or_default())
}

/// Charge un appareil et ses types d'entretien, `None` s'il n'existe pas.
pub async fn load_device(pool: &PgPool, device_id: Uuid) -> AppResult<Option<DeviceSnapshot>> {
    let device: Option<Device> = sqlx::query_as(r#"SELECT * FROM "Devices" WHERE "Id" = $1"#)
        .bind(device_id)
        .fetch_optional(pool)
        .await?;

    let Some(device) = device else {
        return Ok(None);
    };

    let mut types = load_maintenance_types(pool, &[device_id]).await?;
    Ok(Some(DeviceSnapshot {
        maintenance_types: types.remove(&device_id).unwrap_or_default(),
        device,
    }))
}

async fn load_devices_by_house(
    pool: &PgPool,
    house_ids: &[Uuid],
) -> AppResult<HashMap<Uuid, Vec<DeviceSnapshot>>> {
    let devices: Vec<Device> = sqlx::query_as(
        r#"SELECT * FROM "Devices" WHERE "HouseId" = ANY($1) ORDER BY "CreatedAt", "Id""#,
    )
    .bind(house_ids)
    .fetch_all(pool)
    .await?;

    let device_ids: Vec<Uuid> = devices.iter().map(|device| device.id).collect();
    let mut types_by_device = load_maintenance_types(pool, &device_ids).await?;

    let mut by_house: HashMap<Uuid, Vec<DeviceSnapshot>> = HashMap::new();
    for device in devices {
        let maintenance_types = types_by_device.remove(&device.id).unwrap_or_default();
        by_house
            .entry(device.house_id)
            .or_default()
            .push(DeviceSnapshot {
                device,
                maintenance_types,
            });
    }

    Ok(by_house)
}

/// Types d'entretien des appareils demandés, garnis des dates de leurs interventions.
pub async fn load_maintenance_types(
    pool: &PgPool,
    device_ids: &[Uuid],
) -> AppResult<HashMap<Uuid, Vec<MaintenanceTypeSnapshot>>> {
    if device_ids.is_empty() {
        return Ok(HashMap::new());
    }

    let types: Vec<MaintenanceType> = sqlx::query_as(
        r#"SELECT * FROM "MaintenanceTypes" WHERE "DeviceId" = ANY($1) ORDER BY "CreatedAt", "Id""#,
    )
    .bind(device_ids)
    .fetch_all(pool)
    .await?;

    let type_ids: Vec<Uuid> = types.iter().map(|value| value.id).collect();
    let mut dates_by_type = load_instance_dates(pool, &type_ids).await?;

    let mut by_device: HashMap<Uuid, Vec<MaintenanceTypeSnapshot>> = HashMap::new();
    for value in types {
        let instance_dates = dates_by_type.remove(&value.id).unwrap_or_default();
        by_device
            .entry(value.device_id)
            .or_default()
            .push(snapshot_of(&value, instance_dates));
    }

    Ok(by_device)
}

/// Instantané d'un type d'entretien seul (pour les points d'entrée qui n'en chargent qu'un).
pub async fn load_maintenance_type(
    pool: &PgPool,
    type_id: Uuid,
) -> AppResult<Option<MaintenanceTypeSnapshot>> {
    let value: Option<MaintenanceType> =
        sqlx::query_as(r#"SELECT * FROM "MaintenanceTypes" WHERE "Id" = $1"#)
            .bind(type_id)
            .fetch_optional(pool)
            .await?;

    let Some(value) = value else { return Ok(None) };
    let dates = load_instance_dates(pool, &[type_id])
        .await?
        .remove(&type_id)
        .unwrap_or_default();

    Ok(Some(snapshot_of(&value, dates)))
}

fn snapshot_of(
    value: &MaintenanceType,
    instance_dates: Vec<chrono::DateTime<chrono::Utc>>,
) -> MaintenanceTypeSnapshot {
    MaintenanceTypeSnapshot {
        id: value.id,
        name: value.name.clone(),
        periodicity: value.periodicity,
        custom_days: value.custom_days,
        device_id: value.device_id,
        created_at: value.created_at,
        instance_dates,
    }
}

async fn load_instance_dates(
    pool: &PgPool,
    type_ids: &[Uuid],
) -> AppResult<HashMap<Uuid, Vec<chrono::DateTime<chrono::Utc>>>> {
    if type_ids.is_empty() {
        return Ok(HashMap::new());
    }

    let rows: Vec<(Uuid, chrono::DateTime<chrono::Utc>)> = sqlx::query_as(
        r#"SELECT "MaintenanceTypeId", "Date" FROM "MaintenanceInstances"
            WHERE "MaintenanceTypeId" = ANY($1)"#,
    )
    .bind(type_ids)
    .fetch_all(pool)
    .await?;

    let mut by_type: HashMap<Uuid, Vec<chrono::DateTime<chrono::Utc>>> = HashMap::new();
    for (type_id, date) in rows {
        by_type.entry(type_id).or_default().push(date);
    }
    Ok(by_type)
}

/// Interventions complètes des types d'entretien demandés, pour l'historique.
pub async fn load_instances(
    pool: &PgPool,
    type_ids: &[Uuid],
) -> AppResult<Vec<MaintenanceInstance>> {
    if type_ids.is_empty() {
        return Ok(Vec::new());
    }

    Ok(sqlx::query_as(
        r#"SELECT * FROM "MaintenanceInstances" WHERE "MaintenanceTypeId" = ANY($1)
            ORDER BY "CreatedAt", "Id""#,
    )
    .bind(type_ids)
    .fetch_all(pool)
    .await?)
}
