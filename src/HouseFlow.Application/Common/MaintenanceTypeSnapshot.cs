using HouseFlow.Core.Entities;

namespace HouseFlow.Application.Common;

/// <summary>
/// Everything <see cref="HouseFlow.Application.Interfaces.IMaintenanceCalculatorService"/> needs to compute a
/// maintenance type's status, without the full entity graph. Read endpoints project this directly from SQL
/// (periodicity, custom days, and MAX(date) of the instances) instead of loading every <see cref="MaintenanceInstance"/>
/// just to keep the latest one — see issue #218. If the calculator ever needs more than the last instance
/// (e.g. a second-to-last date, or per-instance cost), extend this record explicitly rather than passing entities
/// again: that keeps the coupling between the calculator and its callers visible instead of silent.
/// </summary>
public sealed record MaintenanceTypeSnapshot(
    Guid Id,
    string Name,
    Periodicity Periodicity,
    int? CustomDays,
    Guid DeviceId,
    DateTime CreatedAt,
    DateTime? LastMaintenanceDate
);
