using System.ComponentModel.DataAnnotations;

namespace HouseFlow.Application.DTOs;

public record CreateDeviceRequestDto(
    [Required(ErrorMessage = "Device name is required")]
    [StringLength(200, MinimumLength = 1, ErrorMessage = "Device name must be between 1 and 200 characters")]
    string Name,

    [Required(ErrorMessage = "Device type is required")]
    [StringLength(100, ErrorMessage = "Device type cannot exceed 100 characters")]
    string Type,

    [StringLength(200, ErrorMessage = "Brand cannot exceed 200 characters")]
    string? Brand,

    [StringLength(200, ErrorMessage = "Model cannot exceed 200 characters")]
    string? Model,

    DateTime? InstallDate
);

public record UpdateDeviceRequestDto(
    [StringLength(200, MinimumLength = 1, ErrorMessage = "Device name must be between 1 and 200 characters")]
    string? Name,

    [StringLength(100, ErrorMessage = "Device type cannot exceed 100 characters")]
    string? Type,

    [StringLength(200, ErrorMessage = "Brand cannot exceed 200 characters")]
    string? Brand,

    [StringLength(200, ErrorMessage = "Model cannot exceed 200 characters")]
    string? Model,

    DateTime? InstallDate
);

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
