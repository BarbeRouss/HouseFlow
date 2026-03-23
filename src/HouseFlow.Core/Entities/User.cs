namespace HouseFlow.Core.Entities;

public class User
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public string Theme { get; set; } = "system";
    public string Language { get; set; } = "fr";
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public ICollection<House> Houses { get; set; } = new List<House>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    public ICollection<HouseMember> HouseMemberships { get; set; } = new List<HouseMember>();
}
