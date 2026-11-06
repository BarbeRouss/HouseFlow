namespace HouseFlow.Application.DTOs;

// CreateHouseRequestDto → generated as HouseFlow.Contracts.CreateHouseRequest (see ContractAliases.cs)
// UpdateHouseRequestDto → generated as HouseFlow.Contracts.UpdateHouseRequest (see ContractAliases.cs)

public record HouseDto(
    Guid Id,
    string Name,
    string? Address,
    string? ZipCode,
    string? City,
    DateTime CreatedAt,
    string ColorKey
);

public record HouseSummaryDto(
    Guid Id,
    string Name,
    string? Address,
    string? ZipCode,
    string? City,
    DateTime CreatedAt,
    string ColorKey,
    int Score,
    int DevicesCount,
    int PendingCount,
    int OverdueCount,
    string? UserRole = null,
    string Status = "none", // overdue, pending, up_to_date, none
    int UpToDateCount = 0,
    int MaintenanceTypesCount = 0,
    // One Device.Type per device, in device creation order (C4 card chips).
    IReadOnlyList<string>? DeviceTypes = null,
    // Owner + accepted members (pending invitations excluded) — "Partagée" badge when > 1.
    int MembersCount = 1
);

public record HousesListResponseDto(
    IEnumerable<HouseSummaryDto> Houses,
    int GlobalScore,
    string NextColorKey // HouseColorKey the caller's next house gets without an explicit colorKey (P05 tile)
);

public record HouseDetailDto(
    Guid Id,
    string Name,
    string? Address,
    string? ZipCode,
    string? City,
    DateTime CreatedAt,
    string ColorKey,
    int Score,
    int DevicesCount,
    int PendingCount,
    int OverdueCount,
    IEnumerable<DeviceSummaryDto> Devices,
    string? UserRole,
    string Status,
    int UpToDateCount,
    int MaintenanceTypesCount,
    CapabilitiesDto Capabilities,
    IReadOnlyList<string> DeviceTypes,
    int MembersCount
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
