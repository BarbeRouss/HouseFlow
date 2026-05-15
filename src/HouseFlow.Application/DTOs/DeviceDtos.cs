namespace HouseFlow.Application.DTOs;

// CreateDeviceRequestDto → generated as HouseFlow.Contracts.CreateDeviceRequest (see ContractAliases.cs)
// UpdateDeviceRequestDto → generated as HouseFlow.Contracts.UpdateDeviceRequest (see ContractAliases.cs)

public record DeviceDto(
    Guid Id,
    string Name,
    string Type,
    string? Brand,
    string? Model,
    DateTime? InstallDate,
    Guid HouseId,
    DateTime CreatedAt
);

public record DeviceSummaryDto(
    Guid Id,
    string Name,
    string Type,
    string? Brand,
    string? Model,
    DateTime? InstallDate,
    Guid HouseId,
    DateTime CreatedAt,
    int Score,
    string Status, // up_to_date, pending, overdue
    int PendingCount,
    int MaintenanceTypesCount
);

public record DeviceDetailDto(
    Guid Id,
    string Name,
    string Type,
    string? Brand,
    string? Model,
    DateTime? InstallDate,
    Guid HouseId,
    DateTime CreatedAt,
    int Score,
    string Status,
    int PendingCount,
    int MaintenanceTypesCount,
    IEnumerable<MaintenanceTypeWithStatusDto> MaintenanceTypes,
    decimal TotalSpent,
    int MaintenanceCount
);
