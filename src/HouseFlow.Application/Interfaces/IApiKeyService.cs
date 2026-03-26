using HouseFlow.Application.DTOs;
using HouseFlow.Core.Enums;

namespace HouseFlow.Application.Interfaces;

public interface IApiKeyService
{
    Task<CreateApiKeyResponseDto> CreateAsync(Guid userId, CreateApiKeyRequestDto request, string? ipAddress = null);
    Task<List<ApiKeyDto>> ListAsync(Guid userId);
    Task RevokeAsync(Guid userId, Guid keyId);
    Task<(Guid UserId, ApiKeyScope Scope)?> ValidateKeyAsync(string rawKey);
}
