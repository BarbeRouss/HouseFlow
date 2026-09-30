using System.ComponentModel.DataAnnotations;
using HouseFlow.Application.Common;
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
    int? CustomDays,

    [Range(1, 120, ErrorMessage = "Custom months must be between 1 and 120 (10 years)")]
    int? CustomMonths = null,

    /// <summary>R2 « Dernier entretien » choice; null = Unknown.</summary>
    LastMaintenanceDto? LastMaintenance = null
);

public record UpdateMaintenanceTypeRequestDto(
    [StringLength(200, MinimumLength = 1, ErrorMessage = "Name must be between 1 and 200 characters")]
    string? Name,

    [EnumDataType(typeof(Periodicity), ErrorMessage = "Invalid periodicity")]
    Periodicity? Periodicity,

    [Range(1, 3650, ErrorMessage = "Custom days must be between 1 and 3650 (10 years)")]
    int? CustomDays,

    [Range(1, 120, ErrorMessage = "Custom months must be between 1 and 120 (10 years)")]
    int? CustomMonths = null
);

public record MaintenanceTypeDto(
    Guid Id,
    string Name,
    Periodicity Periodicity,
    int? CustomDays,
    int? CustomMonths,
    Guid DeviceId,
    DateTime CreatedAt
);

public record MaintenanceTypeWithStatusDto(
    Guid Id,
    string Name,
    Periodicity Periodicity,
    int? CustomDays,
    int? CustomMonths,
    Guid DeviceId,
    DateTime CreatedAt,
    string Status, // up_to_date, pending, overdue (R1)
    DateTime? LastMaintenanceDate,
    DateTime NextDueDate // R2 — never null
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
    [NotInFuture(ErrorMessage = "Maintenance date cannot be in the future")]
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
    string Status, // pending, overdue (up_to_date only for DashboardDto.NextTask)
    DateTime NextDueDate,
    DateTime? LastMaintenanceDate,
    string Periodicity,
    int? CustomDays = null,
    int? CustomMonths = null,
    bool CanLogMaintenance = false,
    // The caller's R5 rights on the task's house (e.g. CanViewCosts for M3 opened from P07).
    CapabilitiesDto? Capabilities = null
);

public record UpcomingTasksResponseDto(
    IEnumerable<UpcomingTaskDto> Tasks,
    int OverdueCount,
    int PendingCount
);

/// <summary>Home page (P07): every task to handle + R3 counters + the next up-to-date task.</summary>
public record DashboardDto(
    IEnumerable<UpcomingTaskDto> Tasks,
    int ToHandleCount,
    int OverdueCount,
    int PendingCount,
    int UpToDateCount,
    int TotalCount,
    UpcomingTaskDto? NextTask
);
