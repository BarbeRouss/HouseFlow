namespace HouseFlow.Core.Entities;

public class Device
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Type { get; set; }
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public DateTime? InstallDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public Guid HouseId { get; set; }
    public House? House { get; set; }
    public ICollection<MaintenanceType> MaintenanceTypes { get; set; } = new List<MaintenanceType>();
}
