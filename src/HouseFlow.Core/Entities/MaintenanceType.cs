namespace HouseFlow.Core.Entities;

public class MaintenanceType
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public Periodicity Periodicity { get; set; }
    /// <summary>Legacy custom interval in days (Periodicity = Custom). Mutually exclusive with <see cref="CustomMonths"/>.</summary>
    public int? CustomDays { get; set; }

    /// <summary>Custom interval in calendar months (Periodicity = Custom) — « Tous les n mois / ans » (n ans = 12n mois).</summary>
    public int? CustomMonths { get; set; }

    /// <summary>
    /// How the next due date is derived: « Après la dernière fois » (last record + period, the default) or
    /// « À date fixe » (a calendar grid defined by <see cref="AnchorDay"/> / <see cref="AnchorMonth"/>).
    /// </summary>
    public ScheduleMode ScheduleMode { get; set; } = ScheduleMode.AfterLastTime;

    /// <summary>Day of the month (1-31) of the fixed-date grid; null in <see cref="ScheduleMode.AfterLastTime"/>.</summary>
    public int? AnchorDay { get; set; }

    /// <summary>
    /// Month (1-12) of the fixed-date grid for quarterly, semestrial and annual types (the grid then repeats every
    /// 3, 6 or 12 months from it); null for monthly and in <see cref="ScheduleMode.AfterLastTime"/>.
    /// </summary>
    public int? AnchorMonth { get; set; }

    /// <summary>
    /// R2 — due date applied while the type has no maintenance record: the creation date for
    /// « Plus ancien », creation date + 30 days for « Je ne sais pas ». Stored as the UTC midnight of
    /// the Europe/Paris calendar date. Null for rows created before this column existed: the
    /// « Je ne sais pas » rule (CreatedAt + 30 days) then applies.
    /// </summary>
    public DateTime? BaselineDueDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public Guid DeviceId { get; set; }
    public Device? Device { get; set; }
    public ICollection<MaintenanceInstance> MaintenanceInstances { get; set; } = new List<MaintenanceInstance>();
}

public enum Periodicity
{
    Annual,
    Semestrial,
    Quarterly,
    Monthly,
    Custom,
    // Appended (stored as int): 24 months.
    Biennial
}

public enum ScheduleMode
{
    /// <summary>« Après la dernière fois »: due date = last record + period (default, the historical behaviour).</summary>
    AfterLastTime = 0,

    /// <summary>« À date fixe »: due dates sit on a calendar grid (e.g. the 1st of every month), whatever the real date of the last record.</summary>
    FixedDate = 1
}
