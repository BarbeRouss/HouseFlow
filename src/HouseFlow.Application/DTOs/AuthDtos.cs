using System.ComponentModel.DataAnnotations;

namespace HouseFlow.Application.DTOs;

public record RegisterRequestDto(
    [Required(ErrorMessage = "First name is required")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "First name must be between 1 and 100 characters")]
    string FirstName,

    [Required(ErrorMessage = "Last name is required")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Last name must be between 1 and 100 characters")]
    string LastName,

    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email format")]
    [StringLength(255, ErrorMessage = "Email cannot exceed 255 characters")]
    string Email,

    [Required(ErrorMessage = "Password is required")]
    [StringLength(255, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters")]
    string Password
);

public record LoginRequestDto(
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email format")]
    string Email,

    [Required(ErrorMessage = "Password is required")]
    string Password
);

public record RefreshTokenRequestDto(
    [Required(ErrorMessage = "Refresh token is required")]
    string RefreshToken
);

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
    string Email
);

public record RevokeTokenRequestDto(
    [Required(ErrorMessage = "Token is required")]
    string Token
);
