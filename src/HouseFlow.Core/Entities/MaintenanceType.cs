namespace HouseFlow.Core.Entities;

public class MaintenanceType
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public Periodicity Periodicity { get; set; }
    public int? CustomDays { get; set; } // For custom periodicity
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
    Custom
}
