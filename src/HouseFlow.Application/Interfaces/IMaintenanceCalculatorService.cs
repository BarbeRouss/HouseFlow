using HouseFlow.Application.Common;
using HouseFlow.Application.DTOs;
using HouseFlow.Core.Entities;

namespace HouseFlow.Application.Interfaces;

public interface IMaintenanceCalculatorService
{
    /// <summary>
    /// Calculate the next due date based on periodicity
    /// </summary>
    DateTime CalculateNextDueDate(DateTime lastDate, Periodicity periodicity, int? customDays);

    /// <summary>
    /// Calculate maintenance type status (up_to_date, pending, overdue)
    /// </summary>
    string CalculateMaintenanceTypeStatus(MaintenanceType type, DateTime today);

    /// <summary>
    /// Calculate maintenance type status (up_to_date, pending, overdue) from a projected snapshot
    /// </summary>
    string CalculateMaintenanceTypeStatus(MaintenanceTypeSnapshot snapshot, DateTime today);

    /// <summary>
    /// Calculate device score and status
    /// </summary>
    (int Score, string Status, int PendingCount) CalculateDeviceScore(Device device);

    /// <summary>
    /// Calculate device score and status from projected maintenance type snapshots
    /// </summary>
    (int Score, string Status, int PendingCount) CalculateDeviceScore(IReadOnlyCollection<MaintenanceTypeSnapshot> maintenanceTypes);

    /// <summary>
    /// Calculate house score with pending and overdue counts
    /// </summary>
    (int Score, int PendingCount, int OverdueCount) CalculateHouseScore(House house);

    /// <summary>
    /// Calculate house score with pending and overdue counts from projected maintenance type snapshots
    /// </summary>
    (int Score, int PendingCount, int OverdueCount) CalculateHouseScore(IReadOnlyCollection<MaintenanceTypeSnapshot> maintenanceTypes);

    /// <summary>
    /// Calculate maintenance type with status DTO
    /// </summary>
    MaintenanceTypeWithStatusDto CalculateMaintenanceTypeWithStatus(MaintenanceType type);

    /// <summary>
    /// Calculate maintenance type with status DTO from a projected snapshot
    /// </summary>
    MaintenanceTypeWithStatusDto CalculateMaintenanceTypeWithStatus(MaintenanceTypeSnapshot snapshot);
}
