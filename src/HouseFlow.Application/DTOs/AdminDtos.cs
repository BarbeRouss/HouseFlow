namespace HouseFlow.Application.DTOs;

// SetUserAdminRequestDto → generated as HouseFlow.Contracts.SetUserAdminRequest (see ContractAliases.cs)

public record AdminStatsDto(
    int Users,
    int Admins,
    int Houses,
    int Devices,
    int MaintenanceInstances
);

public record AdminUserDto(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    bool IsAdmin,
    DateTime CreatedAt,
    int HousesCount
);

public record AdminUsersPageDto(
    List<AdminUserDto> Users,
    int Total,
    int Page,
    int PageSize
);
