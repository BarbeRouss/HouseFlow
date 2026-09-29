namespace HouseFlow.Core.Entities;

public class House
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Address { get; set; }
    public string? ZipCode { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    /// <summary>Banner colour, one of <see cref="HouseColors.Palette"/> (assigned in rotation at creation).</summary>
    public string ColorKey { get; set; } = HouseColors.Default;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public ICollection<Device> Devices { get; set; } = new List<Device>();
    public ICollection<HouseMember> Members { get; set; } = new List<HouseMember>();
    public ICollection<Invitation> Invitations { get; set; } = new List<Invitation>();
}
