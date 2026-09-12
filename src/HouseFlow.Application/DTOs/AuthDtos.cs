namespace HouseFlow.Application.DTOs;

// RegisterRequestDto → generated as HouseFlow.Contracts.RegisterRequest (see ContractAliases.cs)
// LoginRequestDto → generated as HouseFlow.Contracts.LoginRequest (see ContractAliases.cs)

public record AuthResponseDto(
    string AccessToken,
    string? RefreshToken,
    int ExpiresIn,
    UserDto User
);

public record UserDto(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string Theme = "system",
    string Language = "fr",
    bool IsAdmin = false
);
