namespace HouseFlow.Core.Entities;

public class MaintenanceInstance
{
    public Guid Id { get; set; }
    public DateTime Date { get; set; }
    public decimal? Cost { get; set; }
    public string? Provider { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public Guid MaintenanceTypeId { get; set; }
    public MaintenanceType? MaintenanceType { get; set; }
}
