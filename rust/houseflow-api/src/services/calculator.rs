//! Calculateur de maintenance — portage pur de `MaintenanceCalculatorService`.
//!
//! Aucune dépendance à la base : les fonctions prennent des instantanés
//! ([`MaintenanceTypeSnapshot`]) que les services de la phase 2 construisent depuis
//! leurs requêtes. Les formes publiques sont figées pour qu'elles puissent être
//! appelées telles quelles.
//!
//! Deux subtilités du C# sont reproduites à l'identique :
//! * `DateTime.AddMonths` / `AddYears` ramènent au dernier jour du mois quand le jour
//!   n'existe pas (29 février → 28 février l'année suivante) ;
//! * `Math.Round` arrondit **au pair le plus proche** (banquier), pas à l'entier
//!   supérieur.

use chrono::{DateTime, Datelike, Duration, NaiveDate, TimeZone, Utc};
use uuid::Uuid;

use crate::models::Periodicity;

/// Statuts renvoyés par le calculateur (chaînes exactes de l'API .NET).
pub const STATUS_UP_TO_DATE: &str = "up_to_date";
pub const STATUS_PENDING: &str = "pending";
pub const STATUS_OVERDUE: &str = "overdue";

/// Fenêtre d'alerte avant échéance, en jours.
const DUE_SOON_DAYS: i64 = 30;

/// Équivalent de l'`ArgumentException` levée pour une périodicité `Custom` sans jours.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum CalculatorError {
    MissingCustomDays,
}

impl std::fmt::Display for CalculatorError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            CalculatorError::MissingCustomDays => f.write_str(
                "customDays is required when periodicity is Custom. (Parameter 'customDays')",
            ),
        }
    }
}

impl std::error::Error for CalculatorError {}

/// Instantané d'un type de maintenance et de ses interventions.
#[derive(Debug, Clone)]
pub struct MaintenanceTypeSnapshot {
    pub id: Uuid,
    pub name: String,
    pub periodicity: Periodicity,
    pub custom_days: Option<i32>,
    pub device_id: Uuid,
    pub created_at: DateTime<Utc>,
    /// Dates des interventions, dans n'importe quel ordre.
    pub instance_dates: Vec<DateTime<Utc>>,
}

impl MaintenanceTypeSnapshot {
    /// Dernière intervention (`OrderByDescending(i => i.Date).FirstOrDefault()`).
    pub fn last_maintenance_date(&self) -> Option<DateTime<Utc>> {
        self.instance_dates.iter().copied().max()
    }
}

/// Score d'un appareil : `(Score, Status, PendingCount)` du C#.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DeviceScore {
    pub score: i32,
    pub status: &'static str,
    pub pending_count: i32,
}

/// Score d'une maison : `(Score, PendingCount, OverdueCount)` du C#.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct HouseScore {
    pub score: i32,
    pub pending_count: i32,
    pub overdue_count: i32,
}

/// Type de maintenance enrichi de son statut (`MaintenanceTypeWithStatusDto`).
#[derive(Debug, Clone, PartialEq)]
pub struct MaintenanceTypeWithStatus {
    pub id: Uuid,
    pub name: String,
    pub periodicity: Periodicity,
    pub custom_days: Option<i32>,
    pub device_id: Uuid,
    pub created_at: DateTime<Utc>,
    pub status: &'static str,
    pub last_maintenance_date: Option<DateTime<Utc>>,
    pub next_due_date: Option<DateTime<Utc>>,
}

/// `DateTime.AddMonths` : décale de `months` mois en ramenant au dernier jour du mois
/// si le jour d'origine n'existe pas dans le mois d'arrivée.
pub fn add_months(value: DateTime<Utc>, months: i32) -> DateTime<Utc> {
    let total = value.year() * 12 + (value.month() as i32 - 1) + months;
    let year = total.div_euclid(12);
    let month = total.rem_euclid(12) as u32 + 1;
    let day = value.day().min(days_in_month(year, month));

    let date = NaiveDate::from_ymd_opt(year, month, day).expect("computed date is valid");
    Utc.from_utc_datetime(&date.and_time(value.time()))
}

/// `DateTime.AddYears`.
pub fn add_years(value: DateTime<Utc>, years: i32) -> DateTime<Utc> {
    add_months(value, years * 12)
}

fn days_in_month(year: i32, month: u32) -> u32 {
    let (next_year, next_month) = if month == 12 {
        (year + 1, 1)
    } else {
        (year, month + 1)
    };
    let first_of_next = NaiveDate::from_ymd_opt(next_year, next_month, 1).expect("valid date");
    let first_of_this = NaiveDate::from_ymd_opt(year, month, 1).expect("valid date");
    (first_of_next - first_of_this).num_days() as u32
}

/// `Math.Round(double)` : arrondi au pair le plus proche (MidpointRounding.ToEven).
pub fn round_half_to_even(value: f64) -> i32 {
    let floor = value.floor();
    let diff = value - floor;
    // Le point milieu va vers l'entier pair ; ailleurs, c'est l'arrondi usuel.
    let rounds_up = diff > 0.5 || (diff == 0.5 && (floor as i64) % 2 != 0);
    let rounded = if rounds_up { floor + 1.0 } else { floor };
    rounded as i32
}

/// Prochaine échéance à partir de la dernière intervention.
pub fn calculate_next_due_date(
    last_date: DateTime<Utc>,
    periodicity: Periodicity,
    custom_days: Option<i32>,
) -> Result<DateTime<Utc>, CalculatorError> {
    Ok(match periodicity {
        Periodicity::Annual => add_years(last_date, 1),
        Periodicity::Semestrial => add_months(last_date, 6),
        Periodicity::Quarterly => add_months(last_date, 3),
        Periodicity::Monthly => add_months(last_date, 1),
        Periodicity::Custom => match custom_days {
            Some(days) => last_date + Duration::days(days as i64),
            None => return Err(CalculatorError::MissingCustomDays),
        },
        // Valeur inconnue : le C# retombe sur « + 1 an » via son bras `_`.
        Periodicity::Unknown(_) => add_years(last_date, 1),
    })
}

/// Statut d'un type de maintenance à la date `today`.
pub fn calculate_maintenance_type_status(
    snapshot: &MaintenanceTypeSnapshot,
    today: DateTime<Utc>,
) -> Result<&'static str, CalculatorError> {
    let Some(last) = snapshot.last_maintenance_date() else {
        return Ok(STATUS_PENDING);
    };

    let next_due = calculate_next_due_date(last, snapshot.periodicity, snapshot.custom_days)?;

    Ok(if next_due < today {
        STATUS_OVERDUE
    } else if next_due <= today + Duration::days(DUE_SOON_DAYS) {
        STATUS_PENDING
    } else {
        STATUS_UP_TO_DATE
    })
}

/// Score d'un appareil à partir de ses types de maintenance.
pub fn calculate_device_score(
    types: &[MaintenanceTypeSnapshot],
) -> Result<DeviceScore, CalculatorError> {
    if types.is_empty() {
        return Ok(DeviceScore {
            score: 100,
            status: STATUS_UP_TO_DATE,
            pending_count: 0,
        });
    }

    let today = today_utc();
    let mut up_to_date = 0;
    let mut pending = 0;
    let mut has_overdue = false;

    for snapshot in types {
        match calculate_maintenance_type_status(snapshot, today)? {
            STATUS_UP_TO_DATE => up_to_date += 1,
            STATUS_PENDING => pending += 1,
            _ => {
                has_overdue = true;
                pending += 1;
            }
        }
    }

    Ok(DeviceScore {
        score: round_half_to_even(f64::from(up_to_date) / types.len() as f64 * 100.0),
        status: if has_overdue {
            STATUS_OVERDUE
        } else if pending > 0 {
            STATUS_PENDING
        } else {
            STATUS_UP_TO_DATE
        },
        pending_count: pending,
    })
}

/// Score d'une maison : agrégation de tous les types de maintenance de ses appareils.
pub fn calculate_house_score(
    all_types: &[MaintenanceTypeSnapshot],
) -> Result<HouseScore, CalculatorError> {
    if all_types.is_empty() {
        return Ok(HouseScore {
            score: 100,
            pending_count: 0,
            overdue_count: 0,
        });
    }

    let today = today_utc();
    let mut up_to_date = 0;
    let mut pending = 0;
    let mut overdue = 0;

    for snapshot in all_types {
        match calculate_maintenance_type_status(snapshot, today)? {
            STATUS_UP_TO_DATE => up_to_date += 1,
            STATUS_PENDING => pending += 1,
            _ => overdue += 1,
        }
    }

    Ok(HouseScore {
        score: round_half_to_even(f64::from(up_to_date) / all_types.len() as f64 * 100.0),
        pending_count: pending,
        overdue_count: overdue,
    })
}

/// Type de maintenance enrichi de son statut et de ses dates.
pub fn calculate_maintenance_type_with_status(
    snapshot: &MaintenanceTypeSnapshot,
) -> Result<MaintenanceTypeWithStatus, CalculatorError> {
    let today = today_utc();
    let last = snapshot.last_maintenance_date();

    let mut next_due_date = None;
    let mut status = STATUS_PENDING;

    if let Some(last) = last {
        let next = calculate_next_due_date(last, snapshot.periodicity, snapshot.custom_days)?;
        next_due_date = Some(next);
        status = if next < today {
            STATUS_OVERDUE
        } else if next <= today + Duration::days(DUE_SOON_DAYS) {
            STATUS_PENDING
        } else {
            STATUS_UP_TO_DATE
        };
    }

    Ok(MaintenanceTypeWithStatus {
        id: snapshot.id,
        name: snapshot.name.clone(),
        periodicity: snapshot.periodicity,
        custom_days: snapshot.custom_days,
        device_id: snapshot.device_id,
        created_at: snapshot.created_at,
        status,
        last_maintenance_date: last,
        next_due_date,
    })
}

/// `DateTime.UtcNow.Date` : minuit UTC du jour courant.
pub fn today_utc() -> DateTime<Utc> {
    let now = Utc::now();
    Utc.from_utc_datetime(
        &now.date_naive()
            .and_hms_opt(0, 0, 0)
            .expect("midnight exists"),
    )
}

#[cfg(test)]
mod tests {
    use super::*;

    fn at(year: i32, month: u32, day: u32) -> DateTime<Utc> {
        Utc.with_ymd_and_hms(year, month, day, 0, 0, 0).unwrap()
    }

    fn maintenance_type(
        periodicity: Periodicity,
        custom_days: Option<i32>,
        instances: Vec<DateTime<Utc>>,
    ) -> MaintenanceTypeSnapshot {
        MaintenanceTypeSnapshot {
            id: Uuid::new_v4(),
            name: "Test Type".into(),
            periodicity,
            custom_days,
            device_id: Uuid::new_v4(),
            created_at: Utc::now(),
            instance_dates: instances,
        }
    }

    // --- CalculateNextDueDate ---

    #[test]
    fn next_due_date_annual_adds_one_year() {
        let result = calculate_next_due_date(at(2025, 3, 15), Periodicity::Annual, None).unwrap();
        assert_eq!(result, at(2026, 3, 15));
    }

    #[test]
    fn next_due_date_semestrial_adds_six_months() {
        let result =
            calculate_next_due_date(at(2025, 1, 10), Periodicity::Semestrial, None).unwrap();
        assert_eq!(result, at(2025, 7, 10));
    }

    #[test]
    fn next_due_date_quarterly_adds_three_months() {
        let result =
            calculate_next_due_date(at(2025, 10, 1), Periodicity::Quarterly, None).unwrap();
        assert_eq!(result, at(2026, 1, 1));
    }

    #[test]
    fn next_due_date_monthly_adds_one_month_and_clamps_the_day() {
        let result = calculate_next_due_date(at(2025, 1, 31), Periodicity::Monthly, None).unwrap();
        assert_eq!(result, at(2025, 2, 28));
    }

    #[test]
    fn next_due_date_custom_adds_the_specified_days() {
        let result =
            calculate_next_due_date(at(2025, 6, 1), Periodicity::Custom, Some(45)).unwrap();
        assert_eq!(result, at(2025, 7, 16));
    }

    #[test]
    fn next_due_date_custom_without_days_is_an_error() {
        let error = calculate_next_due_date(at(2025, 1, 1), Periodicity::Custom, None).unwrap_err();
        assert_eq!(error, CalculatorError::MissingCustomDays);
        assert!(error.to_string().contains("customDays"));
    }

    #[test]
    fn next_due_date_leap_year_feb29_annual_goes_to_feb28() {
        let result = calculate_next_due_date(at(2024, 2, 29), Periodicity::Annual, None).unwrap();
        assert_eq!(result, at(2025, 2, 28));
    }

    #[test]
    fn next_due_date_unknown_periodicity_defaults_to_one_year() {
        let result =
            calculate_next_due_date(at(2025, 5, 1), Periodicity::Unknown(999), None).unwrap();
        assert_eq!(result, at(2026, 5, 1));
    }

    // --- CalculateMaintenanceTypeStatus ---

    #[test]
    fn status_without_instances_is_pending() {
        let snapshot = maintenance_type(Periodicity::Monthly, None, vec![]);
        assert_eq!(
            calculate_maintenance_type_status(&snapshot, today_utc()).unwrap(),
            STATUS_PENDING
        );
    }

    #[test]
    fn status_after_the_due_date_is_overdue() {
        let snapshot = maintenance_type(Periodicity::Monthly, None, vec![at(2026, 2, 1)]);
        assert_eq!(
            calculate_maintenance_type_status(&snapshot, at(2026, 4, 5)).unwrap(),
            STATUS_OVERDUE
        );
    }

    #[test]
    fn status_within_thirty_days_is_pending() {
        let snapshot = maintenance_type(Periodicity::Monthly, None, vec![at(2026, 3, 20)]);
        assert_eq!(
            calculate_maintenance_type_status(&snapshot, at(2026, 4, 5)).unwrap(),
            STATUS_PENDING
        );
    }

    #[test]
    fn status_due_exactly_today_is_pending() {
        let snapshot = maintenance_type(Periodicity::Monthly, None, vec![at(2026, 3, 5)]);
        assert_eq!(
            calculate_maintenance_type_status(&snapshot, at(2026, 4, 5)).unwrap(),
            STATUS_PENDING
        );
    }

    #[test]
    fn status_due_in_exactly_thirty_days_is_pending() {
        let snapshot = maintenance_type(Periodicity::Custom, Some(60), vec![at(2026, 3, 6)]);
        assert_eq!(
            calculate_maintenance_type_status(&snapshot, at(2026, 4, 5)).unwrap(),
            STATUS_PENDING
        );
    }

    #[test]
    fn status_due_beyond_thirty_days_is_up_to_date() {
        let snapshot = maintenance_type(Periodicity::Annual, None, vec![at(2026, 4, 1)]);
        assert_eq!(
            calculate_maintenance_type_status(&snapshot, at(2026, 4, 5)).unwrap(),
            STATUS_UP_TO_DATE
        );
    }

    #[test]
    fn status_uses_the_latest_instance() {
        let snapshot = maintenance_type(
            Periodicity::Annual,
            None,
            vec![at(2024, 1, 1), at(2026, 4, 1)],
        );
        assert_eq!(
            calculate_maintenance_type_status(&snapshot, at(2026, 4, 5)).unwrap(),
            STATUS_UP_TO_DATE
        );
    }

    // --- CalculateDeviceScore ---

    fn recent() -> DateTime<Utc> {
        today_utc() - Duration::days(1)
    }

    fn overdue_monthly() -> DateTime<Utc> {
        add_months(today_utc(), -2)
    }

    #[test]
    fn device_score_without_maintenance_types_is_a_hundred() {
        assert_eq!(
            calculate_device_score(&[]).unwrap(),
            DeviceScore {
                score: 100,
                status: STATUS_UP_TO_DATE,
                pending_count: 0
            }
        );
    }

    #[test]
    fn device_score_all_up_to_date_is_a_hundred() {
        let types = vec![
            maintenance_type(Periodicity::Annual, None, vec![recent()]),
            maintenance_type(Periodicity::Annual, None, vec![recent()]),
        ];
        assert_eq!(
            calculate_device_score(&types).unwrap(),
            DeviceScore {
                score: 100,
                status: STATUS_UP_TO_DATE,
                pending_count: 0
            }
        );
    }

    #[test]
    fn device_score_with_one_overdue_type_is_overdue() {
        let types = vec![
            maintenance_type(Periodicity::Annual, None, vec![recent()]),
            maintenance_type(Periodicity::Monthly, None, vec![overdue_monthly()]),
        ];
        let result = calculate_device_score(&types).unwrap();
        assert_eq!(result.status, STATUS_OVERDUE);
        assert_eq!(result.score, 50);
        assert_eq!(result.pending_count, 1);
    }

    #[test]
    fn device_score_all_pending_without_instances_is_zero() {
        let types = vec![
            maintenance_type(Periodicity::Monthly, None, vec![]),
            maintenance_type(Periodicity::Annual, None, vec![]),
        ];
        assert_eq!(
            calculate_device_score(&types).unwrap(),
            DeviceScore {
                score: 0,
                status: STATUS_PENDING,
                pending_count: 2
            }
        );
    }

    #[test]
    fn device_score_mixes_statuses_with_banker_rounding() {
        let types = vec![
            maintenance_type(Periodicity::Annual, None, vec![recent()]),
            maintenance_type(Periodicity::Annual, None, vec![recent()]),
            maintenance_type(Periodicity::Monthly, None, vec![]),
        ];
        let result = calculate_device_score(&types).unwrap();
        assert_eq!(result.score, 67); // Math.Round(2/3 * 100) = 67
        assert_eq!(result.status, STATUS_PENDING);
        assert_eq!(result.pending_count, 1);
    }

    // --- CalculateHouseScore ---

    #[test]
    fn house_score_without_devices_is_a_hundred() {
        assert_eq!(
            calculate_house_score(&[]).unwrap(),
            HouseScore {
                score: 100,
                pending_count: 0,
                overdue_count: 0
            }
        );
    }

    #[test]
    fn house_score_aggregates_across_devices() {
        let types = vec![
            maintenance_type(Periodicity::Annual, None, vec![recent()]),
            maintenance_type(Periodicity::Monthly, None, vec![overdue_monthly()]),
        ];
        assert_eq!(
            calculate_house_score(&types).unwrap(),
            HouseScore {
                score: 50,
                pending_count: 0,
                overdue_count: 1
            }
        );
    }

    #[test]
    fn house_score_counts_pending_and_overdue_separately() {
        let types = vec![
            maintenance_type(Periodicity::Annual, None, vec![recent()]),
            maintenance_type(Periodicity::Monthly, None, vec![]),
            maintenance_type(Periodicity::Monthly, None, vec![overdue_monthly()]),
        ];
        assert_eq!(
            calculate_house_score(&types).unwrap(),
            HouseScore {
                score: 33, // 1/3 up_to_date
                pending_count: 1,
                overdue_count: 1
            }
        );
    }

    // --- CalculateMaintenanceTypeWithStatus ---

    #[test]
    fn with_status_without_instances_has_null_dates() {
        let mut snapshot = maintenance_type(Periodicity::Monthly, None, vec![]);
        snapshot.name = "Oil Change".into();
        let result = calculate_maintenance_type_with_status(&snapshot).unwrap();
        assert_eq!(result.status, STATUS_PENDING);
        assert!(result.last_maintenance_date.is_none());
        assert!(result.next_due_date.is_none());
        assert_eq!(result.name, "Oil Change");
        assert_eq!(result.periodicity, Periodicity::Monthly);
    }

    #[test]
    fn with_status_reports_both_dates() {
        let instance = today_utc() - Duration::days(10);
        let snapshot = maintenance_type(Periodicity::Monthly, None, vec![instance]);
        let result = calculate_maintenance_type_with_status(&snapshot).unwrap();
        assert_eq!(result.last_maintenance_date, Some(instance));
        assert_eq!(result.next_due_date, Some(add_months(instance, 1)));
    }

    #[test]
    fn with_status_flags_an_overdue_instance() {
        let snapshot = maintenance_type(Periodicity::Monthly, None, vec![overdue_monthly()]);
        assert_eq!(
            calculate_maintenance_type_with_status(&snapshot)
                .unwrap()
                .status,
            STATUS_OVERDUE
        );
    }

    #[test]
    fn with_status_flags_a_recent_instance_as_up_to_date() {
        let snapshot = maintenance_type(Periodicity::Annual, None, vec![recent()]);
        let result = calculate_maintenance_type_with_status(&snapshot).unwrap();
        assert_eq!(result.status, STATUS_UP_TO_DATE);
        assert_eq!(result.next_due_date, Some(add_years(recent(), 1)));
    }

    // --- Détails de portage ---

    #[test]
    fn rounding_follows_the_banker_rule_of_math_round() {
        assert_eq!(round_half_to_even(66.666), 67);
        assert_eq!(round_half_to_even(33.333), 33);
        assert_eq!(round_half_to_even(12.5), 12);
        assert_eq!(round_half_to_even(37.5), 38);
        assert_eq!(round_half_to_even(50.0), 50);
        assert_eq!(round_half_to_even(0.0), 0);
    }

    #[test]
    fn add_months_clamps_to_the_last_day_of_the_month() {
        assert_eq!(add_months(at(2025, 1, 31), 1), at(2025, 2, 28));
        assert_eq!(add_months(at(2024, 1, 31), 1), at(2024, 2, 29));
        assert_eq!(add_months(at(2025, 3, 31), -1), at(2025, 2, 28));
        assert_eq!(add_months(at(2025, 12, 15), 1), at(2026, 1, 15));
        assert_eq!(add_months(at(2025, 1, 15), -1), at(2024, 12, 15));
    }

    #[test]
    fn add_months_keeps_the_time_of_day() {
        let value = Utc.with_ymd_and_hms(2025, 1, 15, 13, 45, 30).unwrap();
        assert_eq!(
            add_months(value, 1),
            Utc.with_ymd_and_hms(2025, 2, 15, 13, 45, 30).unwrap()
        );
    }
}
