//! Journal d'audit — portage de `HouseFlowDbContext.OnBeforeSaveChanges`.
//!
//! Chaque mutation écrit une ligne dans `"AuditLogs"` **dans la transaction de
//! l'appelant**, avec les mêmes champs que le backend .NET :
//!
//! * `EntityType` = nom de la classe C# (`User`, `House`, `RefreshToken`, …) ;
//! * `Action` = `Added` / `Modified` / `Deleted` (noms de `EntityState`) ;
//! * `NewValues` / `OldValues` / `ChangedProperties` = JSON, `NULL` si vide ;
//! * contexte de la requête (utilisateur, IP, user agent) posé par le middleware.

use chrono::Utc;
use serde_json::{Map, Value};
use sqlx::{PgExecutor, Postgres, Transaction};
use uuid::Uuid;

use crate::clock;
use crate::error::AppResult;

/// Contexte d'audit d'une requête (`HouseFlowDbContext.SetAuditContext`).
#[derive(Debug, Clone, Default)]
pub struct AuditContext {
    pub user_id: Option<Uuid>,
    pub username: Option<String>,
    pub ip: Option<String>,
    pub user_agent: Option<String>,
}

impl AuditContext {
    /// Contexte anonyme : aucune identité connue (ex. validation d'une clé API,
    /// qui a lieu avant que le middleware d'audit ne s'exécute côté .NET).
    pub fn anonymous() -> Self {
        Self::default()
    }

    /// Reproduit `SetAuditContext(userId, username, ipAddress)` : les services
    /// d'authentification écrasent l'identité posée par le middleware, et le user
    /// agent repasse à `null` comme en C# (paramètre par défaut).
    pub fn overridden(user_id: Option<Uuid>, username: Option<String>, ip: Option<String>) -> Self {
        Self {
            user_id,
            username,
            ip,
            user_agent: None,
        }
    }
}

/// Valeurs d'une entité, dans l'ordre des propriétés C#.
pub type Values = Map<String, Value>;

/// Construit une carte de valeurs à partir de couples `(propriété, valeur)`.
pub fn values<I>(entries: I) -> Values
where
    I: IntoIterator<Item = (&'static str, Value)>,
{
    entries
        .into_iter()
        .map(|(key, value)| (key.to_string(), value))
        .collect()
}

/// Différence entre deux instantanés d'une même entité : `(anciennes, nouvelles,
/// propriétés modifiées)`.
///
/// Reproduit le tri d'EF (`property.IsModified`) : seules les propriétés dont la
/// valeur change sont consignées, dans l'ordre de déclaration de l'entité.
pub fn diff<'a>(before: &'a Values, after: &'a Values) -> (Values, Values, Vec<&'a str>) {
    let mut old_values = Values::new();
    let mut new_values = Values::new();
    let mut changed = Vec::new();

    for (property, after_value) in after {
        let Some(before_value) = before.get(property) else {
            continue;
        };
        if before_value != after_value {
            old_values.insert(property.clone(), before_value.clone());
            new_values.insert(property.clone(), after_value.clone());
            changed.push(property.as_str());
        }
    }

    (old_values, new_values, changed)
}

/// Sérialise une date comme System.Text.Json le fait pour un `DateTime` UTC.
pub fn date(value: chrono::DateTime<Utc>) -> Value {
    Value::String(value.to_rfc3339_opts(chrono::SecondsFormat::Micros, true))
}

/// Idem pour une date optionnelle.
pub fn opt_date(value: Option<chrono::DateTime<Utc>>) -> Value {
    value.map(date).unwrap_or(Value::Null)
}

/// Sérialise un identifiant (`Guid` → chaîne).
pub fn id(value: Uuid) -> Value {
    Value::String(value.to_string())
}

/// Idem pour un identifiant optionnel.
pub fn opt_id(value: Option<Uuid>) -> Value {
    value.map(id).unwrap_or(Value::Null)
}

/// Sérialise une chaîne optionnelle.
pub fn opt_str(value: Option<&str>) -> Value {
    value
        .map(|v| Value::String(v.to_string()))
        .unwrap_or(Value::Null)
}

#[allow(clippy::too_many_arguments)]
async fn insert<'e, E: PgExecutor<'e>>(
    executor: E,
    context: &AuditContext,
    entity_type: &str,
    entity_id: &str,
    action: &str,
    old_values: Option<&Values>,
    new_values: Option<&Values>,
    changed_properties: Option<&[&str]>,
) -> AppResult<()> {
    let to_json = |values: Option<&Values>| -> Option<String> {
        values
            .filter(|v| !v.is_empty())
            .map(|v| serde_json::to_string(v).unwrap_or_else(|_| "{}".to_string()))
    };
    let changed = changed_properties
        .filter(|c| !c.is_empty())
        .map(|c| serde_json::to_string(c).unwrap_or_else(|_| "[]".to_string()));

    sqlx::query(
        r#"INSERT INTO "AuditLogs"
               ("Id", "EntityType", "EntityId", "Action", "UserId", "Username", "Timestamp",
                "OldValues", "NewValues", "ChangedProperties", "IpAddress", "UserAgent", "AdditionalData")
           VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, NULL)"#,
    )
    .bind(Uuid::new_v4())
    .bind(entity_type)
    .bind(entity_id)
    .bind(action)
    .bind(context.user_id)
    .bind(context.username.as_deref())
    .bind(clock::now())
    .bind(to_json(old_values))
    .bind(to_json(new_values))
    .bind(changed)
    .bind(context.ip.as_deref())
    .bind(context.user_agent.as_deref())
    .execute(executor)
    .await?;

    Ok(())
}

/// Entité créée (`EntityState.Added`).
pub async fn record_added(
    tx: &mut Transaction<'_, Postgres>,
    context: &AuditContext,
    entity_type: &str,
    entity_id: Uuid,
    new_values: &Values,
) -> AppResult<()> {
    insert(
        &mut **tx,
        context,
        entity_type,
        &entity_id.to_string(),
        "Added",
        None,
        Some(new_values),
        None,
    )
    .await
}

/// Entité modifiée (`EntityState.Modified`) : seules les propriétés changées sont listées.
pub async fn record_modified(
    tx: &mut Transaction<'_, Postgres>,
    context: &AuditContext,
    entity_type: &str,
    entity_id: Uuid,
    old_values: &Values,
    new_values: &Values,
    changed_properties: &[&str],
) -> AppResult<()> {
    insert(
        &mut **tx,
        context,
        entity_type,
        &entity_id.to_string(),
        "Modified",
        Some(old_values),
        Some(new_values),
        Some(changed_properties),
    )
    .await
}

/// Entité supprimée (`EntityState.Deleted`) : aucune suppression douce n'existe dans
/// ce schéma, ce sont de vrais DELETE.
pub async fn record_deleted(
    tx: &mut Transaction<'_, Postgres>,
    context: &AuditContext,
    entity_type: &str,
    entity_id: Uuid,
    old_values: &Values,
) -> AppResult<()> {
    insert(
        &mut **tx,
        context,
        entity_type,
        &entity_id.to_string(),
        "Deleted",
        Some(old_values),
        None,
        None,
    )
    .await
}

/// Variante hors transaction, pour les écritures isolées (validation d'une clé API,
/// amorçage des administrateurs au démarrage).
pub async fn record_modified_standalone<'e, E: PgExecutor<'e>>(
    executor: E,
    context: &AuditContext,
    entity_type: &str,
    entity_id: Uuid,
    old_values: &Values,
    new_values: &Values,
    changed_properties: &[&str],
) -> AppResult<()> {
    insert(
        executor,
        context,
        entity_type,
        &entity_id.to_string(),
        "Modified",
        Some(old_values),
        Some(new_values),
        Some(changed_properties),
    )
    .await
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn values_keep_the_declared_property_order() {
        let map = values([
            ("Id", id(Uuid::nil())),
            ("Email", Value::String("a@b.com".into())),
            ("IsAdmin", Value::Bool(false)),
        ]);
        let json = serde_json::to_string(&map).unwrap();
        assert_eq!(
            json,
            r#"{"Id":"00000000-0000-0000-0000-000000000000","Email":"a@b.com","IsAdmin":false}"#
        );
    }

    #[test]
    fn dates_are_serialized_as_utc_iso8601() {
        let moment = chrono::DateTime::parse_from_rfc3339("2026-09-18T21:30:00Z")
            .unwrap()
            .with_timezone(&Utc);
        assert_eq!(
            date(moment),
            Value::String("2026-09-18T21:30:00.000000Z".into())
        );
        assert_eq!(opt_date(None), Value::Null);
    }

    #[test]
    fn overridden_context_drops_the_user_agent_like_csharp() {
        let context =
            AuditContext::overridden(None, Some("a@b.com".into()), Some("127.0.0.1".into()));
        assert!(context.user_agent.is_none());
        assert_eq!(context.username.as_deref(), Some("a@b.com"));
    }
}
