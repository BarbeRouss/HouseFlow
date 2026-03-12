using HouseFlow.Application.DTOs;
using HouseFlow.Application.Interfaces;
using HouseFlow.Core.Entities;

namespace HouseFlow.Infrastructure.Services;

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
            Periodicity.Custom => lastDate.AddDays(customDays ?? 365),
            _ => lastDate.AddYears(1)
        };
    }

    public string CalculateMaintenanceTypeStatus(MaintenanceType type, DateTime today)
    {
        var lastMaintenance = type.MaintenanceInstances
            .OrderByDescending(i => i.Date)
            .FirstOrDefault();

        if (lastMaintenance == null)
        {
            return "pending";
        }

        var nextDueDate = CalculateNextDueDate(lastMaintenance.Date, type.Periodicity, type.CustomDays);

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

    public MaintenanceTypeWithStatusDto CalculateMaintenanceTypeWithStatus(MaintenanceType type)
    {
        var today = DateTime.UtcNow.Date;
        var lastMaintenance = type.MaintenanceInstances
            .OrderByDescending(i => i.Date)
            .FirstOrDefault();

        DateTime? lastDate = lastMaintenance?.Date;
        DateTime? nextDueDate = null;
        string status = "pending";

        if (lastMaintenance != null)
        {
            nextDueDate = CalculateNextDueDate(lastMaintenance.Date, type.Periodicity, type.CustomDays);

            if (nextDueDate < today)
            {
                status = "overdue";
            }
            else if (nextDueDate <= today.AddDays(30))
            {
                status = "pending";
            }
            else
            {
                status = "up_to_date";
            }
        }

        return new MaintenanceTypeWithStatusDto(
            type.Id,
            type.Name,
            type.Periodicity,
            type.CustomDays,
            type.DeviceId,
            type.CreatedAt,
            status,
            lastDate,
            nextDueDate
        );
    }
}
