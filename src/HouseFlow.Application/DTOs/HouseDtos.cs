namespace HouseFlow.Application.DTOs;

// CreateHouseRequestDto → generated as HouseFlow.Contracts.CreateHouseRequest (see ContractAliases.cs)
// UpdateHouseRequestDto → generated as HouseFlow.Contracts.UpdateHouseRequest (see ContractAliases.cs)

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
