using System.ComponentModel.DataAnnotations;

namespace HouseFlow.Application.DTOs;

public record CreateApiKeyRequestDto(
    [Required(ErrorMessage = "Name is required")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Name must be between 1 and 100 characters")]
    string Name,

    string Scope = "ReadWrite"
);

public record CreateApiKeyResponseDto(
    Guid Id,
    string Name,
    string Key,
    string Prefix,
    string Scope,
    DateTime CreatedAt
);

public record ApiKeyDto(
    Guid Id,
    string Name,
    string Prefix,
    string Scope,
    DateTime CreatedAt,
    DateTime? LastUsedAt
);
