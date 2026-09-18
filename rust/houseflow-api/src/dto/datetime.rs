//! Lecture des dates de requête, à la façon du backend .NET.
//!
//! Les contrats générés (`Contracts.g.cs`) attachent un `DateFormatConverter` aux
//! champs `date` / `installDate` : il **écrit** `yyyy-MM-dd` et **lit** avec
//! `DateTime.Parse`, qui accepte aussi bien `2026-09-18` que `2026-09-18T10:30:00`
//! ou une forme complète avec décalage. Les clients du dépôt (dont la suite
//! d'intégration) envoient donc une date nue là où d'autres DTO envoient une date
//! ISO complète : les deux doivent être acceptées.
//!
//! Une date sans décalage est considérée comme UTC, exactement comme
//! `HouseFlowDbContext` le fait de son côté (`DateTimeKind.Unspecified` ⇒ `Utc`).

use chrono::{DateTime, NaiveDate, NaiveDateTime, TimeZone, Utc};
use serde::{Deserialize, Deserializer};

/// Formats acceptés pour une date-heure sans décalage.
const NAIVE_FORMATS: &[&str] = &[
    "%Y-%m-%dT%H:%M:%S%.f",
    "%Y-%m-%dT%H:%M:%S",
    "%Y-%m-%dT%H:%M",
    "%Y-%m-%d %H:%M:%S%.f",
    "%Y-%m-%d %H:%M:%S",
    "%Y-%m-%d %H:%M",
];

/// Analyse une date façon `DateTime.Parse`, ramenée en UTC.
pub fn parse(raw: &str) -> Option<DateTime<Utc>> {
    let raw = raw.trim();

    // Forme complète avec décalage (`…Z`, `…+02:00`).
    if let Ok(value) = DateTime::parse_from_rfc3339(raw) {
        return Some(value.with_timezone(&Utc));
    }

    for format in NAIVE_FORMATS {
        if let Ok(value) = NaiveDateTime::parse_from_str(raw, format) {
            return Some(Utc.from_utc_datetime(&value));
        }
    }

    // Date nue : minuit UTC, comme `DateTime.Parse("2026-09-18")`.
    NaiveDate::parse_from_str(raw, "%Y-%m-%d")
        .ok()
        .and_then(|date| date.and_hms_opt(0, 0, 0))
        .map(|value| Utc.from_utc_datetime(&value))
}

/// Désérialise une date optionnelle (`null` et champ absent ⇒ `None`).
pub fn deserialize_optional<'de, D>(deserializer: D) -> Result<Option<DateTime<Utc>>, D::Error>
where
    D: Deserializer<'de>,
{
    let Some(raw) = Option::<String>::deserialize(deserializer)? else {
        return Ok(None);
    };

    parse(&raw)
        .map(Some)
        .ok_or_else(|| serde::de::Error::custom(format!("The string '{raw}' is not a valid date.")))
}

#[cfg(test)]
mod tests {
    use super::*;

    fn at(year: i32, month: u32, day: u32, hour: u32, minute: u32, second: u32) -> DateTime<Utc> {
        Utc.with_ymd_and_hms(year, month, day, hour, minute, second)
            .unwrap()
    }

    #[test]
    fn a_bare_date_becomes_midnight_utc() {
        assert_eq!(parse("2026-09-18").unwrap(), at(2026, 9, 18, 0, 0, 0));
    }

    #[test]
    fn an_iso_instant_keeps_its_time() {
        assert_eq!(
            parse("2026-09-18T10:30:00Z").unwrap(),
            at(2026, 9, 18, 10, 30, 0)
        );
        assert_eq!(
            parse("2026-09-18T10:30:00.1234567Z").unwrap(),
            at(2026, 9, 18, 10, 30, 0) + chrono::Duration::nanoseconds(123_456_700)
        );
    }

    #[test]
    fn an_offset_is_converted_to_utc() {
        assert_eq!(
            parse("2026-09-18T12:30:00+02:00").unwrap(),
            at(2026, 9, 18, 10, 30, 0)
        );
    }

    #[test]
    fn a_date_without_offset_is_read_as_utc() {
        assert_eq!(
            parse("2026-09-18T10:30:00").unwrap(),
            at(2026, 9, 18, 10, 30, 0)
        );
    }

    #[test]
    fn nonsense_is_rejected() {
        assert!(parse("not-a-date").is_none());
        assert!(parse("").is_none());
    }
}
