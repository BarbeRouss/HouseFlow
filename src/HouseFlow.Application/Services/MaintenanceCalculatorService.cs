using HouseFlow.Application.Common;
using HouseFlow.Application.DTOs;
using HouseFlow.Application.Interfaces;
using HouseFlow.Core.Entities;

namespace HouseFlow.Application.Services;

public class MaintenanceCalculatorService : IMaintenanceCalculatorService
{
    public DateTime CalculateNextDueDate(DateTime lastDate, Periodicity periodicity, int? customDays)
    {
        return periodicity switch
        {
            Periodicity.Annual => lastDate.AddYears(1),
            Periodicity.Semestrial => lastDate.AddMonths(6),
            Periodicity.Quarterly => lastDate.AddMonths(3),
            Periodicity.Monthly => lastDate.AddMonths(1),
            Periodicity.Custom when customDays.HasValue => lastDate.AddDays(customDays.Value),
            Periodicity.Custom => throw new ArgumentException(
                "customDays is required when periodicity is Custom.", nameof(customDays)),
            _ => lastDate.AddYears(1)
        };
    }

    public string CalculateMaintenanceTypeStatus(MaintenanceType type, DateTime today)
    {
        var lastMaintenance = type.MaintenanceInstances
            .OrderByDescending(i => i.Date)
            .FirstOrDefault();

        return CalculateStatus(type.Periodicity, type.CustomDays, lastMaintenance?.Date, today);
    }

    public string CalculateMaintenanceTypeStatus(MaintenanceTypeSnapshot snapshot, DateTime today)
        => CalculateStatus(snapshot.Periodicity, snapshot.CustomDays, snapshot.LastMaintenanceDate, today);

    private string CalculateStatus(Periodicity periodicity, int? customDays, DateTime? lastMaintenanceDate, DateTime today)
    {
        if (lastMaintenanceDate == null)
        {
            return "pending";
        }

        var nextDueDate = CalculateNextDueDate(lastMaintenanceDate.Value, periodicity, customDays);

        if (nextDueDate < today)
        {
            return "overdue";
        }
        else if (nextDueDate <= today.AddDays(30))
        {
            return "pending";
        }

        return "up_to_date";
    }

    public (int Score, string Status, int PendingCount) CalculateDeviceScore(Device device)
    {
        if (device.MaintenanceTypes.Count == 0)
        {
            return (100, "up_to_date", 0);
        }

        var today = DateTime.UtcNow.Date;
        var upToDateCount = 0;
        var pendingCount = 0;
        var hasOverdue = false;

        foreach (var type in device.MaintenanceTypes)
        {
            var status = CalculateMaintenanceTypeStatus(type, today);
            switch (status)
            {
                case "up_to_date":
                    upToDateCount++;
                    break;
                case "pending":
                    pendingCount++;
                    break;
                case "overdue":
                    hasOverdue = true;
                    pendingCount++;
                    break;
            }
        }

        var score = (int)Math.Round((double)upToDateCount / device.MaintenanceTypes.Count * 100);
        var overallStatus = hasOverdue ? "overdue" : (pendingCount > 0 ? "pending" : "up_to_date");

        return (score, overallStatus, pendingCount);
    }

    public (int Score, string Status, int PendingCount) CalculateDeviceScore(IReadOnlyCollection<MaintenanceTypeSnapshot> maintenanceTypes)
    {
        if (maintenanceTypes.Count == 0)
        {
            return (100, "up_to_date", 0);
        }

        var today = DateTime.UtcNow.Date;
        var upToDateCount = 0;
        var pendingCount = 0;
        var hasOverdue = false;

        foreach (var snapshot in maintenanceTypes)
        {
            var status = CalculateMaintenanceTypeStatus(snapshot, today);
            switch (status)
            {
                case "up_to_date":
                    upToDateCount++;
                    break;
                case "pending":
                    pendingCount++;
                    break;
                case "overdue":
                    hasOverdue = true;
                    pendingCount++;
                    break;
            }
        }

        var score = (int)Math.Round((double)upToDateCount / maintenanceTypes.Count * 100);
        var overallStatus = hasOverdue ? "overdue" : (pendingCount > 0 ? "pending" : "up_to_date");

        return (score, overallStatus, pendingCount);
    }

    public (int Score, int PendingCount, int OverdueCount) CalculateHouseScore(House house)
    {
        var allTypes = house.Devices
            .SelectMany(d => d.MaintenanceTypes)
            .ToList();

        if (allTypes.Count == 0)
        {
            return (100, 0, 0);
        }

        var today = DateTime.UtcNow.Date;
        var upToDateCount = 0;
        var pendingCount = 0;
        var overdueCount = 0;

        foreach (var type in allTypes)
        {
            var status = CalculateMaintenanceTypeStatus(type, today);
            switch (status)
            {
                case "up_to_date":
                    upToDateCount++;
                    break;
                case "pending":
                    pendingCount++;
                    break;
                case "overdue":
                    overdueCount++;
                    break;
            }
        }

        var score = (int)Math.Round((double)upToDateCount / allTypes.Count * 100);
        return (score, pendingCount, overdueCount);
    }

    public (int Score, int PendingCount, int OverdueCount) CalculateHouseScore(IReadOnlyCollection<MaintenanceTypeSnapshot> maintenanceTypes)
    {
        if (maintenanceTypes.Count == 0)
        {
            return (100, 0, 0);
        }

        var today = DateTime.UtcNow.Date;
        var upToDateCount = 0;
        var pendingCount = 0;
        var overdueCount = 0;

        foreach (var snapshot in maintenanceTypes)
        {
            var status = CalculateMaintenanceTypeStatus(snapshot, today);
            switch (status)
            {
                case "up_to_date":
                    upToDateCount++;
                    break;
                case "pending":
                    pendingCount++;
                    break;
                case "overdue":
                    overdueCount++;
                    break;
            }
        }

        var score = (int)Math.Round((double)upToDateCount / maintenanceTypes.Count * 100);
        return (score, pendingCount, overdueCount);
    }

    public MaintenanceTypeWithStatusDto CalculateMaintenanceTypeWithStatus(MaintenanceType type)
    {
        var lastMaintenance = type.MaintenanceInstances
            .OrderByDescending(i => i.Date)
            .FirstOrDefault();

        return BuildMaintenanceTypeWithStatus(
            type.Id, type.Name, type.Periodicity, type.CustomDays, type.DeviceId, type.CreatedAt, lastMaintenance?.Date);
    }

    public MaintenanceTypeWithStatusDto CalculateMaintenanceTypeWithStatus(MaintenanceTypeSnapshot snapshot)
        => BuildMaintenanceTypeWithStatus(
            snapshot.Id, snapshot.Name, snapshot.Periodicity, snapshot.CustomDays, snapshot.DeviceId, snapshot.CreatedAt,
            snapshot.LastMaintenanceDate);

    private MaintenanceTypeWithStatusDto BuildMaintenanceTypeWithStatus(
        Guid id, string name, Periodicity periodicity, int? customDays, Guid deviceId, DateTime createdAt, DateTime? lastMaintenanceDate)
    {
        var today = DateTime.UtcNow.Date;
        DateTime? nextDueDate = null;
        var status = "pending";

        if (lastMaintenanceDate != null)
        {
            nextDueDate = CalculateNextDueDate(lastMaintenanceDate.Value, periodicity, customDays);
            status = nextDueDate < today ? "overdue" : nextDueDate <= today.AddDays(30) ? "pending" : "up_to_date";
        }

        return new MaintenanceTypeWithStatusDto(
            id,
            name,
            periodicity,
            customDays,
            deviceId,
            createdAt,
            status,
            lastMaintenanceDate,
            nextDueDate
        );
    }
}
