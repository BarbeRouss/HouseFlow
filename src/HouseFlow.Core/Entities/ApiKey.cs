using HouseFlow.Core.Enums;

namespace HouseFlow.Core.Entities;

public class ApiKey
{
    public Guid Id { get; set; }
    public required Guid UserId { get; set; }
    public required string Name { get; set; }
    public required string Prefix { get; set; }
    public required string KeyHash { get; set; }
    public ApiKeyScope Scope { get; set; } = ApiKeyScope.ReadWrite;
    public DateTime CreatedAt { get; set; }
    public string? CreatedByIp { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public bool IsActive => RevokedAt == null;

    // Navigation property
    public User? User { get; set; }
}
