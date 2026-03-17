using HouseFlow.Core.Enums;

namespace HouseFlow.Core.Entities;

public class HouseMember
{
    public Guid Id { get; set; }
    public HouseRole Role { get; set; }
    public bool CanLogMaintenance { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid HouseId { get; set; }
    public House? House { get; set; }
}
