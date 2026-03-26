using System.Security.Cryptography;
using System.Text;
using HouseFlow.Application.DTOs;
using HouseFlow.Application.Interfaces;
using HouseFlow.Core.Entities;
using HouseFlow.Core.Enums;
using HouseFlow.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HouseFlow.Infrastructure.Services;

public class ApiKeyService : IApiKeyService
{
    private const int MaxKeysPerUser = 5;
    private const string KeyPrefix = "hf_";
    private const int RandomBytesLength = 32;
    // Base62 characters (URL-safe, no padding)
    private const string Base62Chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    private readonly HouseFlowDbContext _context;
    private readonly ILogger<ApiKeyService> _logger;

    public ApiKeyService(HouseFlowDbContext context, ILogger<ApiKeyService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<CreateApiKeyResponseDto> CreateAsync(Guid userId, CreateApiKeyRequestDto request, string? ipAddress = null)
    {
        // Parse scope
        if (!Enum.TryParse<ApiKeyScope>(request.Scope, ignoreCase: true, out var scope))
        {
            throw new InvalidOperationException("Invalid scope. Must be 'ReadOnly' or 'ReadWrite'.");
        }

        // Check limit
        var activeCount = await _context.ApiKeys
            .CountAsync(k => k.UserId == userId && k.RevokedAt == null);

        if (activeCount >= MaxKeysPerUser)
        {
            throw new InvalidOperationException($"Maximum of {MaxKeysPerUser} active API keys allowed. Revoke an existing key before creating a new one.");
        }

        // Generate key
        var rawKeyBody = GenerateBase62Token(RandomBytesLength);
        var fullKey = $"{KeyPrefix}{rawKeyBody}";
        var prefix = fullKey[..11]; // "hf_" + 8 chars
        var keyHash = ComputeSha256Hash(fullKey);

        var apiKey = new ApiKey
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = request.Name,
            Prefix = prefix,
            KeyHash = keyHash,
            Scope = scope,
            CreatedAt = DateTime.UtcNow,
            CreatedByIp = ipAddress
        };

        _context.ApiKeys.Add(apiKey);
        await _context.SaveChangesAsync();

        _logger.LogInformation("API key created for user {UserId}: {Prefix}", userId, prefix);

        return new CreateApiKeyResponseDto(
            apiKey.Id,
            apiKey.Name,
            fullKey,
            prefix,
            scope.ToString(),
            apiKey.CreatedAt
        );
    }

    public async Task<List<ApiKeyDto>> ListAsync(Guid userId)
    {
        return await _context.ApiKeys
            .Where(k => k.UserId == userId && k.RevokedAt == null)
            .OrderByDescending(k => k.CreatedAt)
            .Select(k => new ApiKeyDto(
                k.Id,
                k.Name,
                k.Prefix,
                k.Scope.ToString(),
                k.CreatedAt,
                k.LastUsedAt
            ))
            .ToListAsync();
    }

    public async Task RevokeAsync(Guid userId, Guid keyId)
    {
        var apiKey = await _context.ApiKeys
            .FirstOrDefaultAsync(k => k.Id == keyId && k.UserId == userId);

        if (apiKey == null)
        {
            throw new KeyNotFoundException("API key not found.");
        }

        if (apiKey.RevokedAt != null)
        {
            throw new InvalidOperationException("API key is already revoked.");
        }

        apiKey.RevokedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("API key revoked: {Prefix} for user {UserId}", apiKey.Prefix, userId);
    }

    public async Task<(Guid UserId, ApiKeyScope Scope)?> ValidateKeyAsync(string rawKey)
    {
        if (string.IsNullOrEmpty(rawKey) || !rawKey.StartsWith(KeyPrefix) || rawKey.Length < 11)
        {
            return null;
        }

        var prefix = rawKey[..11];
        var keyHash = ComputeSha256Hash(rawKey);

        var apiKey = await _context.ApiKeys
            .FirstOrDefaultAsync(k => k.Prefix == prefix && k.KeyHash == keyHash && k.RevokedAt == null);

        if (apiKey == null)
        {
            return null;
        }

        // Update last used timestamp
        apiKey.LastUsedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return (apiKey.UserId, apiKey.Scope);
    }

    private static string GenerateBase62Token(int byteLength)
    {
        var randomBytes = new byte[byteLength];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);

        var sb = new StringBuilder(byteLength * 2);
        foreach (var b in randomBytes)
        {
            sb.Append(Base62Chars[b % Base62Chars.Length]);
        }
        return sb.ToString();
    }

    private static string ComputeSha256Hash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }
}
