using HouseFlow.Application.DTOs;

namespace HouseFlow.Application.Interfaces;

public interface IAuthService
{
    Task<AuthResponseDto> RegisterAsync(RegisterRequestDto request, string? ipAddress = null, string? invitationToken = null);
    Task<AuthResponseDto> LoginAsync(LoginRequestDto request, string? ipAddress = null);
    Task<AuthResponseDto> RefreshTokenAsync(string refreshToken, string? ipAddress = null);
    Task RevokeTokenAsync(string token, string? ipAddress = null);
    string GenerateJwtToken(Guid userId, string email);
}
