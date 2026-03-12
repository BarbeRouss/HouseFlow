using HouseFlow.Application.DTOs;
using HouseFlow.Application.Interfaces;
using HouseFlow.Core.Entities;
using HouseFlow.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HouseFlow.Infrastructure.Services;

public class DeviceService : IDeviceService
{
    private readonly HouseFlowDbContext _context;
    private readonly IMaintenanceCalculatorService _calculator;

    public DeviceService(HouseFlowDbContext context, IMaintenanceCalculatorService calculator)
    {
        _context = context;
        _calculator = calculator;
    }

    public async Task<IEnumerable<DeviceSummaryDto>> GetHouseDevicesAsync(Guid houseId, Guid userId)
    {
        await ValidateHouseAccessAsync(houseId, userId);

        var devices = await _context.Devices
            .AsNoTracking()
            .Where(d => d.HouseId == houseId)
            .Include(d => d.MaintenanceTypes)
                .ThenInclude(mt => mt.MaintenanceInstances)
            .ToListAsync();

        return devices.Select(d => CalculateDeviceSummary(d));
    }

    public async Task<DeviceDetailDto?> GetDeviceDetailAsync(Guid deviceId, Guid userId)
    {
        var device = await _context.Devices
            .AsNoTracking()
            .Include(d => d.MaintenanceTypes)
                .ThenInclude(mt => mt.MaintenanceInstances)
            .FirstOrDefaultAsync(d => d.Id == deviceId);

        if (device == null)
        {
            return null;
        }

        await ValidateHouseAccessAsync(device.HouseId, userId);

        return CalculateDeviceDetail(device);
    }

    public async Task<DeviceDto> CreateDeviceAsync(Guid houseId, CreateDeviceRequestDto request, Guid userId)
    {
        await ValidateHouseAccessAsync(houseId, userId);

        var device = new Device
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Type = request.Type,
            Brand = request.Brand,
            Model = request.Model,
            InstallDate = request.InstallDate,
            HouseId = houseId,
            CreatedAt = DateTime.UtcNow
        };

        _context.Devices.Add(device);
        await _context.SaveChangesAsync();

        return new DeviceDto(
            device.Id,
            device.Name,
            device.Type,
            device.Brand,
            device.Model,
            device.InstallDate,
            device.HouseId,
            device.CreatedAt
        );
    }

    public async Task<DeviceDto?> UpdateDeviceAsync(Guid deviceId, UpdateDeviceRequestDto request, Guid userId)
    {
        var device = await _context.Devices
            .FirstOrDefaultAsync(d => d.Id == deviceId);

        if (device == null)
        {
            return null;
        }

        await ValidateHouseAccessAsync(device.HouseId, userId);

        if (request.Name != null) device.Name = request.Name;
        if (request.Type != null) device.Type = request.Type;
        if (request.Brand != null) device.Brand = request.Brand;
        if (request.Model != null) device.Model = request.Model;
        if (request.InstallDate != null) device.InstallDate = request.InstallDate;
        device.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return new DeviceDto(
            device.Id,
            device.Name,
            device.Type,
            device.Brand,
            device.Model,
            device.InstallDate,
            device.HouseId,
            device.CreatedAt
        );
    }

    public async Task<bool> DeleteDeviceAsync(Guid deviceId, Guid userId)
    {
        var device = await _context.Devices
            .FirstOrDefaultAsync(d => d.Id == deviceId);

        if (device == null)
        {
            return false;
        }

        await ValidateHouseAccessAsync(device.HouseId, userId);

        _context.Devices.Remove(device);
        await _context.SaveChangesAsync();
        return true;
    }

    private async Task ValidateHouseAccessAsync(Guid houseId, Guid userId)
    {
        var hasAccess = await _context.Houses
            .AnyAsync(h => h.Id == houseId && h.UserId == userId);

        if (!hasAccess)
        {
            throw new UnauthorizedAccessException("Access denied to this house");
        }
    }

    private DeviceSummaryDto CalculateDeviceSummary(Device device)
    {
        var (score, status, pendingCount) = _calculator.CalculateDeviceScore(device);

        return new DeviceSummaryDto(
            device.Id,
            device.Name,
            device.Type,
            device.Brand,
            device.Model,
            device.InstallDate,
            device.HouseId,
            device.CreatedAt,
            score,
            status,
            pendingCount,
            device.MaintenanceTypes.Count
        );
    }

    private DeviceDetailDto CalculateDeviceDetail(Device device)
    {
        var (score, status, pendingCount) = _calculator.CalculateDeviceScore(device);
        var maintenanceTypes = device.MaintenanceTypes.Select(mt => _calculator.CalculateMaintenanceTypeWithStatus(mt)).ToList();
        var totalSpent = device.MaintenanceTypes
            .SelectMany(mt => mt.MaintenanceInstances)
            .Sum(i => i.Cost ?? 0);
        var maintenanceCount = device.MaintenanceTypes
            .SelectMany(mt => mt.MaintenanceInstances)
            .Count();

        return new DeviceDetailDto(
            device.Id,
            device.Name,
            device.Type,
            device.Brand,
            device.Model,
            device.InstallDate,
            device.HouseId,
            device.CreatedAt,
            score,
            status,
            pendingCount,
            device.MaintenanceTypes.Count,
            maintenanceTypes,
            totalSpent,
            maintenanceCount
        );
    }
}
