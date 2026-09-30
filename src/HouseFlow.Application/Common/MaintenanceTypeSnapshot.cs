using HouseFlow.Core.Entities;

namespace HouseFlow.Application.Common;

/// <summary>
/// Everything <see cref="HouseFlow.Application.Interfaces.IMaintenanceCalculatorService"/> needs to compute a
/// maintenance type's status, without the full entity graph. Read endpoints project this directly from SQL
/// (periodicity, custom interval, no-history baseline and MAX(date) of the instances) instead of loading every
/// <see cref="MaintenanceInstance"/> just to keep the latest one — see issue #218. If the calculator ever needs
/// more than the last instance, extend this record explicitly rather than passing entities again: that keeps the
/// coupling between the calculator and its callers visible instead of silent.
/// </summary>
public sealed record MaintenanceTypeSnapshot(
    Guid Id,
    string Name,
    Periodicity Periodicity,
    int? CustomDays,
    int? CustomMonths,
    Guid DeviceId,
    DateTime CreatedAt,
    DateTime? BaselineDueDate,
    DateTime? LastMaintenanceDate
)
{
    /// <summary>Snapshot of an entity whose <see cref="MaintenanceType.MaintenanceInstances"/> are loaded.</summary>
    public static MaintenanceTypeSnapshot From(MaintenanceType type) => new(
        type.Id, type.Name, type.Periodicity, type.CustomDays, type.CustomMonths, type.DeviceId, type.CreatedAt,
        type.BaselineDueDate,
        type.MaintenanceInstances.Count == 0 ? null : type.MaintenanceInstances.Max(i => i.Date));
}

/// <summary>
/// Status counts of a set of maintenance types (a device, a house, or everything a user can see) — R1/R3.
/// <see cref="Status"/> is the most urgent status, or <c>none</c> when there is no type at all.
/// </summary>
public sealed record MaintenanceStatusSummary(
    int Total,
    int UpToDate,
    int Pending,
    int Overdue,
    string Status,
    int Score
);

/// <summary>R1 status values shared by maintenance types, devices and houses.</summary>
public static class MaintenanceStatuses
{
    public const string Overdue = "overdue";
    public const string Pending = "pending";
    public const string UpToDate = "up_to_date";

    /// <summary>Device/house with no maintenance type.</summary>
    public const string None = "none";
}
