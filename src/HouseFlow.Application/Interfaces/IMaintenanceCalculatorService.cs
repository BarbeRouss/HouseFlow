using HouseFlow.Application.Common;
using HouseFlow.Application.DTOs;
using HouseFlow.Core.Entities;

namespace HouseFlow.Application.Interfaces;

/// <summary>
/// Single source of the product rules R1 (status, 30-day window, Europe/Paris "today") and R2 (next due date).
/// </summary>
public interface IMaintenanceCalculatorService
{
    /// <summary>R1 window: a type is "pending" (À faire) when due within this share of its period (min. 1 day).</summary>
    const double DueSoonWindowRatio = 0.10;

    /// <summary>R2 "Je ne sais pas": a type without history is due this many days after its creation.</summary>
    const int UnknownHistoryDelayDays = 30;

    /// <summary>Today's date in Europe/Paris (UTC-midnight value).</summary>
    DateTime Today { get; }

    /// <summary>Last maintenance date + periodicity, in calendar months (or days for legacy custom intervals).</summary>
    DateTime CalculateNextDueDate(DateTime lastDate, Periodicity periodicity, int? customDays, int? customMonths = null);

    /// <summary>
    /// R2 next due date of a type, never null: from the last record when there is one, else the stored
    /// no-history baseline, else (legacy rows) creation date + 30 days.
    /// </summary>
    DateTime CalculateNextDueDate(MaintenanceTypeSnapshot snapshot);

    /// <summary>R1 status of a due date: overdue / pending / up_to_date. The "pending" window is 10 % of the type's period.</summary>
    string CalculateStatus(DateTime nextDueDate, DateTime today, Periodicity periodicity, int? customDays, int? customMonths = null);

    /// <summary>Counts and most urgent status of a set of types (device, house, dashboard).</summary>
    MaintenanceStatusSummary Summarize(IEnumerable<MaintenanceTypeSnapshot> maintenanceTypes);

    /// <summary>Maintenance type with its R1 status and R2 dates.</summary>
    MaintenanceTypeWithStatusDto CalculateMaintenanceTypeWithStatus(MaintenanceTypeSnapshot snapshot);

    /// <summary>The most urgent type of a set (earliest next due date), or null when the set is empty.</summary>
    MaintenanceTypeWithStatusDto? MostUrgent(IEnumerable<MaintenanceTypeSnapshot> maintenanceTypes);

    /// <summary>Baseline due date stored at creation for a type without history (R2).</summary>
    DateTime NoHistoryBaseline(DateTime createdAtUtc, bool olderThanKnown);
}
