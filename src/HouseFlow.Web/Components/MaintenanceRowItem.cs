using HouseFlow.Web.Api;
using HouseFlow.Web.Rules;

namespace HouseFlow.Web.Components;

/// <summary>Where a C3 row is shown: P07 (menu = « Fait à une autre date… » only, click → P10) or P10.</summary>
public enum MaintenanceRowContext
{
    Dashboard,
    Device,
}

/// <summary>
/// One maintenance type as displayed by a C3 row (<see cref="MaintenanceRow"/>) and handled by
/// <see cref="MaintenanceActions"/>. Carries the R5 rights of the current user for its house, so
/// the row hides what the role forbids (never disabled).
/// </summary>
public sealed record MaintenanceRowItem
{
    public required string TypeId { get; init; }
    public required string Name { get; init; }
    public required string DeviceId { get; init; }
    public required string DeviceName { get; init; }
    public string? HouseName { get; init; }
    public string? Status { get; init; }
    public string? NextDueDate { get; init; }
    public string? LastMaintenanceDate { get; init; }
    public string Periodicity { get; init; } = PeriodicityRules.Annual;
    public int? CustomMonths { get; init; }
    public int? CustomDays { get; init; }

    /// <summary>« C'est fait », M3 create / edit (owner, RW, tenant with the right).</summary>
    public bool CanLog { get; init; }

    /// <summary>M4 edit (owner, RW).</summary>
    public bool CanEdit { get; init; }

    /// <summary>Delete the type (M6) or a record — also C5 « Annuler » (owner, RW).</summary>
    public bool CanDelete { get; init; }

    /// <summary>Provider and cost fields in M3.</summary>
    public bool CanViewCosts { get; init; } = true;

    public DueStatus? DueStatus => StatusRules.FromApi(Status) ?? StatusRules.Compute(NextDueDate);

    /// <summary>P07 row: rights from the task's <see cref="UpcomingTask.Capabilities"/> (none → read-only).</summary>
    public static MaintenanceRowItem From(UpcomingTask t) => new()
    {
        TypeId = t.MaintenanceTypeId,
        Name = t.MaintenanceTypeName,
        DeviceId = t.DeviceId,
        DeviceName = t.DeviceName,
        HouseName = t.HouseName,
        Status = t.Status,
        NextDueDate = t.NextDueDate,
        LastMaintenanceDate = t.LastMaintenanceDate,
        Periodicity = t.Periodicity,
        CustomMonths = t.CustomMonths,
        CustomDays = t.CustomDays,
        CanLog = t.CanLogMaintenance,
        CanEdit = t.Capabilities?.CanEditDevices ?? false,
        CanDelete = t.Capabilities?.CanDelete ?? false,
        CanViewCosts = t.Capabilities?.CanViewCosts ?? false,
    };

    public static MaintenanceRowItem From(MaintenanceTypeWithStatus t, DeviceDetail device) => new()
    {
        TypeId = t.Id,
        Name = t.Name,
        DeviceId = device.Id,
        DeviceName = device.Name,
        HouseName = device.HouseName,
        Status = t.Status,
        NextDueDate = t.NextDueDate,
        LastMaintenanceDate = t.LastMaintenanceDate,
        Periodicity = t.Periodicity,
        CustomMonths = t.CustomMonths,
        CustomDays = t.CustomDays,
        CanLog = device.Capabilities.CanLogMaintenance,
        CanEdit = device.Capabilities.CanEditDevices,
        CanDelete = device.Capabilities.CanDelete,
        CanViewCosts = device.Capabilities.CanViewCosts,
    };

    /// <summary>
    /// R2 next due date once a record dated <paramref name="done"/> exists: counted from the latest
    /// record (an older back-dated record does not move the due date).
    /// </summary>
    public DateOnly NextDueAfter(DateOnly done)
    {
        var last = DateFormatter.ParseDate(LastMaintenanceDate);
        var from = last is { } l && l > done ? l : done;
        return PeriodicityRules.NextDue(from, Periodicity, CustomMonths, CustomDays);
    }
}
