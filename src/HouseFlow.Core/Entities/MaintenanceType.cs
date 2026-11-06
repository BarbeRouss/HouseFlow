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
