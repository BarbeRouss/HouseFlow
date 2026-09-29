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
    string? UserRole = null,
    string Status = "none", // overdue, pending, up_to_date, none
    int UpToDateCount = 0,
    int MaintenanceTypesCount = 0
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
    string? UserRole,
    string Status,
    int UpToDateCount,
    int MaintenanceTypesCount,
    CapabilitiesDto Capabilities
);

/// <summary>
/// The caller's rights on a house (rule R5), so the UI can hide — never disable — what it may not do.
/// </summary>
public record CapabilitiesDto(
    bool CanLogMaintenance,
    bool CanEditDevices,
    bool CanDelete,
    bool CanManageHouse,
    bool CanManageMembers,
    bool CanInviteTenants,
    bool CanViewCosts
);
