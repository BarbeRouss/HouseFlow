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
    string Status, // overdue, pending, up_to_date, none
    int PendingCount, // due within 30 days, overdue excluded
    int MaintenanceTypesCount,
    int OverdueCount = 0,
    int UpToDateCount = 0,
    // Most urgent maintenance (earliest next due date) — P09 C4 row: relative date + its name as subtitle
    // when overdue / due. Null when the device has no maintenance type.
    DateTime? NextDueDate = null,
    string? NextMaintenanceName = null
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
    int MaintenanceCount,
    int OverdueCount,
    int UpToDateCount,
    string HouseName,
    string UserRole,
    CapabilitiesDto Capabilities
);
