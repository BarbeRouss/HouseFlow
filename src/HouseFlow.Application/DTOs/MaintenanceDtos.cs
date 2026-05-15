using System.ComponentModel.DataAnnotations;
using HouseFlow.Core.Entities;

namespace HouseFlow.Application.DTOs;

public record CreateMaintenanceTypeRequestDto(
    [Required(ErrorMessage = "Maintenance type name is required")]
    [StringLength(200, MinimumLength = 1, ErrorMessage = "Name must be between 1 and 200 characters")]
    string Name,

    [Required(ErrorMessage = "Periodicity is required")]
    [EnumDataType(typeof(Periodicity), ErrorMessage = "Invalid periodicity")]
    Periodicity Periodicity,

    [Range(1, 3650, ErrorMessage = "Custom days must be between 1 and 3650 (10 years)")]
    int? CustomDays
);

public record UpdateMaintenanceTypeRequestDto(
    [StringLength(200, MinimumLength = 1, ErrorMessage = "Name must be between 1 and 200 characters")]
    string? Name,

    [EnumDataType(typeof(Periodicity), ErrorMessage = "Invalid periodicity")]
    Periodicity? Periodicity,

    [Range(1, 3650, ErrorMessage = "Custom days must be between 1 and 3650 (10 years)")]
    int? CustomDays
);

public record MaintenanceTypeDto(
    Guid Id,
    string Name,
    Periodicity Periodicity,
    int? CustomDays,
    Guid DeviceId,
    DateTime CreatedAt
);

public record MaintenanceTypeWithStatusDto(
    Guid Id,
    string Name,
    Periodicity Periodicity,
    int? CustomDays,
    Guid DeviceId,
    DateTime CreatedAt,
    string Status, // up_to_date, pending, overdue
    DateTime? LastMaintenanceDate,
    DateTime? NextDueDate
);

// LogMaintenanceRequestDto → generated as HouseFlow.Contracts.LogMaintenanceRequest (see ContractAliases.cs)

public record MaintenanceInstanceDto(
    Guid Id,
    DateTime Date,
    decimal? Cost,
    string? Provider,
    string? Notes,
    Guid MaintenanceTypeId,
    string MaintenanceTypeName,
    DateTime CreatedAt
);

public record MaintenanceHistoryResponseDto(
    IEnumerable<MaintenanceInstanceDto> Instances,
    decimal TotalSpent,
    int Count
);

public record UpdateMaintenanceInstanceRequestDto(
    DateTime? Date,

    [Range(0, 1000000, ErrorMessage = "Cost must be between 0 and 1,000,000")]
    decimal? Cost,

    [StringLength(200, ErrorMessage = "Provider name cannot exceed 200 characters")]
    string? Provider,

    [StringLength(2000, ErrorMessage = "Notes cannot exceed 2000 characters")]
    string? Notes
);

public record UpcomingTaskDto(
    Guid MaintenanceTypeId,
    string MaintenanceTypeName,
    Guid DeviceId,
    string DeviceName,
    string DeviceType,
    Guid HouseId,
    string HouseName,
    string Status, // pending, overdue
    DateTime? NextDueDate,
    DateTime? LastMaintenanceDate,
    string Periodicity
);

public record UpcomingTasksResponseDto(
    IEnumerable<UpcomingTaskDto> Tasks,
    int OverdueCount,
    int PendingCount
);
