namespace HouseFlow.Core.Entities;

public class House
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Address { get; set; }
    public string? ZipCode { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public ICollection<Device> Devices { get; set; } = new List<Device>();
}
