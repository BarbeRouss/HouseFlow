using HouseFlow.Web.Api;

namespace HouseFlow.Web.Components;

/// <summary>
/// « Dernier entretien » value edited by <see cref="LastMaintenancePicker"/> (P06, M2, M4 — R2).
/// Maps 1:1 to the API <c>lastMaintenance {kind, year, month}</c>. A year without a month is an
/// incomplete choice (<see cref="IsComplete"/> false): the form must stay disabled.
/// </summary>
public sealed record LastMaintenanceChoice(string Kind, int? Year = null, int? Month = null)
{
    /// <summary>« Je ne sais pas » — the default.</summary>
    public static LastMaintenanceChoice Unknown { get; } = new(LastMaintenance.KindUnknown);

    /// <summary>« Plus ancien ».</summary>
    public static LastMaintenanceChoice Older { get; } = new(LastMaintenance.KindOlder);

    public static LastMaintenanceChoice InMonth(int year, int? month) => new(LastMaintenance.KindMonth, year, month);

    public bool IsMonth => Kind == LastMaintenance.KindMonth;

    /// <summary>False while a year is chosen without its month (« Choisissez le mois »).</summary>
    public bool IsComplete => !IsMonth || (Year is not null && Month is not null);

    /// <summary>Request payload; only call when <see cref="IsComplete"/>.</summary>
    public LastMaintenance ToRequest() => Kind switch
    {
        LastMaintenance.KindOlder => LastMaintenance.Older(),
        LastMaintenance.KindMonth when Year is { } y && Month is { } m => LastMaintenance.InMonth(y, m),
        _ => LastMaintenance.Unknown(),
    };

    /// <summary>First day of the chosen month (R2 approximate record date), null otherwise.</summary>
    public DateOnly? ApproximateDate => IsMonth && Year is { } y && Month is { } m ? new DateOnly(y, m, 1) : null;
}
