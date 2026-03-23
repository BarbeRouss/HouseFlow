using System.ComponentModel.DataAnnotations;

namespace HouseFlow.Application.DTOs;

public record CreateHouseRequestDto(
    [Required(ErrorMessage = "House name is required")]
    [StringLength(200, MinimumLength = 1, ErrorMessage = "House name must be between 1 and 200 characters")]
    string Name,

    [StringLength(500, ErrorMessage = "Address cannot exceed 500 characters")]
    string? Address,

    [StringLength(20, ErrorMessage = "Zip code cannot exceed 20 characters")]
    string? ZipCode,

    [StringLength(200, ErrorMessage = "City cannot exceed 200 characters")]
    string? City
);

public record UpdateHouseRequestDto(
    [StringLength(200, MinimumLength = 1, ErrorMessage = "House name must be between 1 and 200 characters")]
    string? Name,

    [StringLength(500, ErrorMessage = "Address cannot exceed 500 characters")]
    string? Address,

    [StringLength(20, ErrorMessage = "Zip code cannot exceed 20 characters")]
    string? ZipCode,

    [StringLength(200, ErrorMessage = "City cannot exceed 200 characters")]
    string? City
);

public record HouseDto(
    Guid Id,
    string Name,
    string? Address,
    string? ZipCode,
    string? City,
    DateTime CreatedAt
);

public record HouseSummaryDto(
    Guid Id,
    string Name,
    string? Address,
    string? ZipCode,
    string? City,
    DateTime CreatedAt,
    int Score,
    int DevicesCount,
    int PendingCount,
    int OverdueCount,
    string? UserRole = null
);

public record HousesListResponseDto(
    IEnumerable<HouseSummaryDto> Houses,
    int GlobalScore
);

public record HouseDetailDto(
    Guid Id,
    string Name,
    string? Address,
    string? ZipCode,
    string? City,
    DateTime CreatedAt,
    int Score,
    int DevicesCount,
    int PendingCount,
    int OverdueCount,
    IEnumerable<DeviceSummaryDto> Devices,
    string? UserRole = null
);
