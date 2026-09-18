//! Horloge du serveur.
//!
//! `timestamp with time zone` ne stocke que la microseconde ; `Utc::now()` va jusqu'à
//! la nanoseconde. Sans troncature, une valeur renvoyée dans une réponse ne
//! correspond pas à celle relue ensuite en base (`createdAt` d'une clé API, par
//! exemple). Toutes les écritures passent donc par [`now`].

use chrono::{DateTime, SubsecRound, Utc};

/// Instant courant, tronqué à la précision de PostgreSQL.
pub fn now() -> DateTime<Utc> {
    Utc::now().trunc_subsecs(6)
}

#[cfg(test)]
mod tests {
    use super::*;
    use chrono::Timelike;

    #[test]
    fn now_is_truncated_to_microseconds() {
        assert_eq!(now().nanosecond() % 1_000, 0);
    }

    #[test]
    fn now_round_trips_through_an_rfc3339_microsecond_rendering() {
        let value = now();
        let rendered = value.to_rfc3339_opts(chrono::SecondsFormat::Micros, true);
        let parsed = DateTime::parse_from_rfc3339(&rendered).unwrap().to_utc();
        assert_eq!(parsed, value);
    }
}
