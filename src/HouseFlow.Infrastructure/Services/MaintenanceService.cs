using HouseFlow.Application.DTOs;
using HouseFlow.Application.Interfaces;
using HouseFlow.Core.Entities;
using HouseFlow.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HouseFlow.Infrastructure.Services;

public class MaintenanceService : IMaintenanceService
{
    private readonly HouseFlowDbContext _context;
    private readonly IMaintenanceCalculatorService _calculator;

    public MaintenanceService(HouseFlowDbContext context, IMaintenanceCalculatorService calculator)
    {
        _context = context;
        _calculator = calculator;
    }

    public async Task<IEnumerable<MaintenanceTypeWithStatusDto>> GetDeviceMaintenanceTypesAsync(Guid deviceId, Guid userId)
    {
        var device = await _context.Devices
            .AsNoTracking()
            .Include(d => d.MaintenanceTypes)
                .ThenInclude(mt => mt.MaintenanceInstances)
            .FirstOrDefaultAsync(d => d.Id == deviceId);

        if (device == null)
        {
            throw new KeyNotFoundException("Device not found");
        }

        await ValidateDeviceAccessAsync(device.HouseId, userId);

        return device.MaintenanceTypes.Select(mt => _calculator.CalculateMaintenanceTypeWithStatus(mt));
    }

    public async Task<MaintenanceTypeDto> CreateMaintenanceTypeAsync(Guid deviceId, CreateMaintenanceTypeRequestDto request, Guid userId)
    {
        var device = await _context.Devices
            .FirstOrDefaultAsync(d => d.Id == deviceId);

        if (device == null)
        {
            throw new KeyNotFoundException("Device not found");
        }

        await ValidateDeviceAccessAsync(device.HouseId, userId);

        var maintenanceType = new MaintenanceType
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Periodicity = request.Periodicity,
            CustomDays = request.CustomDays,
            DeviceId = deviceId,
            CreatedAt = DateTime.UtcNow
        };

        _context.MaintenanceTypes.Add(maintenanceType);
        await _context.SaveChangesAsync();

        return new MaintenanceTypeDto(
            maintenanceType.Id,
            maintenanceType.Name,
            maintenanceType.Periodicity,
            maintenanceType.CustomDays,
            maintenanceType.DeviceId,
            maintenanceType.CreatedAt
        );
    }

    public async Task<MaintenanceTypeDto?> UpdateMaintenanceTypeAsync(Guid typeId, UpdateMaintenanceTypeRequestDto request, Guid userId)
    {
        var maintenanceType = await _context.MaintenanceTypes
            .Include(mt => mt.Device)
            .FirstOrDefaultAsync(mt => mt.Id == typeId);

        if (maintenanceType?.Device == null)
        {
            return null;
        }

        await ValidateDeviceAccessAsync(maintenanceType.Device.HouseId, userId);

        if (request.Name != null) maintenanceType.Name = request.Name;
        if (request.Periodicity != null) maintenanceType.Periodicity = request.Periodicity.Value;
        if (request.CustomDays != null) maintenanceType.CustomDays = request.CustomDays;
        maintenanceType.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return new MaintenanceTypeDto(
            maintenanceType.Id,
            maintenanceType.Name,
            maintenanceType.Periodicity,
            maintenanceType.CustomDays,
            maintenanceType.DeviceId,
            maintenanceType.CreatedAt
        );
    }

    public async Task<bool> DeleteMaintenanceTypeAsync(Guid typeId, Guid userId)
    {
        var maintenanceType = await _context.MaintenanceTypes
            .Include(mt => mt.Device)
            .FirstOrDefaultAsync(mt => mt.Id == typeId);

        if (maintenanceType?.Device == null)
        {
            return false;
        }

        await ValidateDeviceAccessAsync(maintenanceType.Device.HouseId, userId);

        _context.MaintenanceTypes.Remove(maintenanceType);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<MaintenanceInstanceDto> LogMaintenanceAsync(Guid typeId, LogMaintenanceRequestDto request, Guid userId)
    {
        var maintenanceType = await _context.MaintenanceTypes
            .Include(mt => mt.Device)
            .FirstOrDefaultAsync(mt => mt.Id == typeId);

        if (maintenanceType?.Device == null)
        {
            throw new KeyNotFoundException("Maintenance type not found");
        }

        await ValidateDeviceAccessAsync(maintenanceType.Device.HouseId, userId);

        var instance = new MaintenanceInstance
        {
            Id = Guid.NewGuid(),
            Date = request.Date,
            Cost = request.Cost,
            Provider = request.Provider,
            Notes = request.Notes,
            MaintenanceTypeId = typeId,
            CreatedAt = DateTime.UtcNow
        };

        _context.MaintenanceInstances.Add(instance);
        await _context.SaveChangesAsync();

        return new MaintenanceInstanceDto(
            instance.Id,
            instance.Date,
            instance.Cost,
            instance.Provider,
            instance.Notes,
            instance.MaintenanceTypeId,
            maintenanceType.Name,
            instance.CreatedAt
        );
    }

    public async Task<MaintenanceHistoryResponseDto> GetDeviceMaintenanceHistoryAsync(Guid deviceId, Guid userId)
    {
        var device = await _context.Devices
            .AsNoTracking()
            .Include(d => d.MaintenanceTypes)
                .ThenInclude(mt => mt.MaintenanceInstances)
            .FirstOrDefaultAsync(d => d.Id == deviceId);

        if (device == null)
        {
            throw new KeyNotFoundException("Device not found");
        }

        await ValidateDeviceAccessAsync(device.HouseId, userId);

        var instances = device.MaintenanceTypes
            .SelectMany(mt => mt.MaintenanceInstances.Select(i => new MaintenanceInstanceDto(
                i.Id,
                i.Date,
                i.Cost,
                i.Provider,
                i.Notes,
                i.MaintenanceTypeId,
                mt.Name,
                i.CreatedAt
            )))
            .OrderByDescending(i => i.Date)
            .ToList();

        var totalSpent = instances.Sum(i => i.Cost ?? 0);

        return new MaintenanceHistoryResponseDto(instances, totalSpent, instances.Count);
    }

    public async Task<MaintenanceInstanceDto?> UpdateMaintenanceInstanceAsync(Guid instanceId, UpdateMaintenanceInstanceRequestDto request, Guid userId)
    {
        var instance = await _context.MaintenanceInstances
            .Include(i => i.MaintenanceType)
                .ThenInclude(mt => mt!.Device)
            .FirstOrDefaultAsync(i => i.Id == instanceId);

        if (instance?.MaintenanceType?.Device == null)
        {
            return null;
        }

        await ValidateDeviceAccessAsync(instance.MaintenanceType.Device.HouseId, userId);

        if (request.Date != null) instance.Date = request.Date.Value;
        if (request.Cost != null) instance.Cost = request.Cost;
        if (request.Provider != null) instance.Provider = request.Provider;
        if (request.Notes != null) instance.Notes = request.Notes;
        instance.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return new MaintenanceInstanceDto(
            instance.Id,
            instance.Date,
            instance.Cost,
            instance.Provider,
            instance.Notes,
            instance.MaintenanceTypeId,
            instance.MaintenanceType.Name,
            instance.CreatedAt
        );
    }

    public async Task<bool> DeleteMaintenanceInstanceAsync(Guid instanceId, Guid userId)
    {
        var instance = await _context.MaintenanceInstances
            .Include(i => i.MaintenanceType)
                .ThenInclude(mt => mt!.Device)
            .FirstOrDefaultAsync(i => i.Id == instanceId);

        if (instance?.MaintenanceType?.Device == null)
        {
            return false;
        }

        await ValidateDeviceAccessAsync(instance.MaintenanceType.Device.HouseId, userId);

        _context.MaintenanceInstances.Remove(instance);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<UpcomingTasksResponseDto> GetUpcomingTasksAsync(Guid userId, int? limit = null)
    {
        var houses = await _context.Houses
            .AsNoTracking()
            .Where(h => h.UserId == userId)
            .Include(h => h.Devices)
                .ThenInclude(d => d.MaintenanceTypes)
                    .ThenInclude(mt => mt.MaintenanceInstances)
            .ToListAsync();

        var tasks = new List<UpcomingTaskDto>();

        foreach (var house in houses)
        {
            foreach (var device in house.Devices)
            {
                foreach (var mt in device.MaintenanceTypes)
                {
                    var withStatus = _calculator.CalculateMaintenanceTypeWithStatus(mt);

                    if (withStatus.Status is "pending" or "overdue")
                    {
                        tasks.Add(new UpcomingTaskDto(
                            mt.Id,
                            mt.Name,
                            device.Id,
                            device.Name,
                            device.Type,
                            house.Id,
                            house.Name,
                            withStatus.Status,
                            withStatus.NextDueDate,
                            withStatus.LastMaintenanceDate,
                            mt.Periodicity.ToString()
                        ));
                    }
                }
            }
        }

        // Sort: tasks never done (null NextDueDate) first, then overdue by date ASC, then pending by date ASC
        var sorted = tasks
            .OrderBy(t => t.NextDueDate == null ? 0 : 1)
            .ThenBy(t => t.Status == "overdue" ? 0 : 1)
            .ThenBy(t => t.NextDueDate ?? DateTime.MaxValue)
            .ToList();

        var overdueCount = sorted.Count(t => t.Status == "overdue");
        var pendingCount = sorted.Count(t => t.Status == "pending");

        var result = limit.HasValue ? sorted.Take(limit.Value).ToList() : sorted;

        return new UpcomingTasksResponseDto(result, overdueCount, pendingCount);
    }

    private async Task ValidateDeviceAccessAsync(Guid houseId, Guid userId)
    {
        var hasAccess = await _context.Houses.AnyAsync(h => h.Id == houseId && h.UserId == userId);
        if (!hasAccess)
        {
            throw new UnauthorizedAccessException("Access denied to this device");
        }
    }
}
