namespace HouseFlow.Application.DTOs;

// RegisterRequestDto → generated as HouseFlow.Contracts.RegisterRequest (see ContractAliases.cs)
// LoginRequestDto → generated as HouseFlow.Contracts.LoginRequest (see ContractAliases.cs)

public record AuthResponseDto(
    string AccessToken,
    string? RefreshToken,
    int ExpiresIn,
    UserDto User,
    /// <summary>
    /// Expiry of the persistent refresh-token cookie ("remember me"); null means a
    /// session cookie. Never serialized to clients (the controller blanks it, as it
    /// does for RefreshToken).
    /// </summary>
    DateTime? RefreshCookieExpiresAt = null
);

public record UserDto(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string Theme = "system",
    string Language = "fr",
    bool ConsentRequired = false,
    bool IsAdmin = false
);
