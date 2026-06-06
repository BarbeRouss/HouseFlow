namespace HouseFlow.Frontend.Wasm.Models;

// DTOs mirroring specs/openapi.yaml (Auth + Houses). POC scope only.

public sealed record LoginRequest(string Email, string Password);

public sealed record AuthResponse
{
    public string? AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public int ExpiresIn { get; init; }
    public UserDto? User { get; init; }
}

public sealed record UserDto
{
    public Guid Id { get; init; }
    public string FirstName { get; init; } = "";
    public string LastName { get; init; } = "";
    public string Email { get; init; } = "";
    public string? Theme { get; init; }
    public string? Language { get; init; }
}

public sealed record HouseSummary
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string? Address { get; init; }
    public string? ZipCode { get; init; }
    public string? City { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public int Score { get; init; }
    public int DevicesCount { get; init; }
    public int PendingCount { get; init; }
    public int OverdueCount { get; init; }
    public string? UserRole { get; init; }
}

public sealed record HousesListResponse
{
    public List<HouseSummary> Houses { get; init; } = new();
    public int GlobalScore { get; init; }
}
