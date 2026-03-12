using HouseFlow.Application.DTOs;
using HouseFlow.Application.Interfaces;
using HouseFlow.Core.Entities;
using HouseFlow.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HouseFlow.Infrastructure.Services;

public class HouseService : IHouseService
{
    private readonly HouseFlowDbContext _context;
    private readonly IMaintenanceCalculatorService _calculator;

    public HouseService(HouseFlowDbContext context, IMaintenanceCalculatorService calculator)
    {
        _context = context;
        _calculator = calculator;
    }

    public async Task<HousesListResponseDto> GetUserHousesAsync(Guid userId)
    {
        var houses = await _context.Houses
            .AsNoTracking()
            .Where(h => h.UserId == userId)
            .Include(h => h.Devices)
                .ThenInclude(d => d.MaintenanceTypes)
                    .ThenInclude(mt => mt.MaintenanceInstances)
            .ToListAsync();

        var houseSummaries = houses.Select(h => CalculateHouseSummary(h)).ToList();

        var globalScore = houseSummaries.Count > 0
            ? (int)Math.Round(houseSummaries.Average(h => h.Score))
            : 100;

        return new HousesListResponseDto(houseSummaries, globalScore);
    }

    public async Task<HouseDetailDto?> GetHouseDetailAsync(Guid houseId, Guid userId)
    {
        var house = await _context.Houses
            .AsNoTracking()
            .Where(h => h.Id == houseId && h.UserId == userId)
            .Include(h => h.Devices)
                .ThenInclude(d => d.MaintenanceTypes)
                    .ThenInclude(mt => mt.MaintenanceInstances)
            .FirstOrDefaultAsync();

        if (house == null)
        {
            return null;
        }

        var deviceSummaries = house.Devices.Select(d => CalculateDeviceSummary(d)).ToList();
        var (score, pendingCount, overdueCount) = _calculator.CalculateHouseScore(house);

        return new HouseDetailDto(
            house.Id,
            house.Name,
            house.Address,
            house.ZipCode,
            house.City,
            house.CreatedAt,
            score,
            house.Devices.Count,
            pendingCount,
            overdueCount,
            deviceSummaries
        );
    }

    public async Task<HouseDto> CreateHouseAsync(CreateHouseRequestDto request, Guid userId)
    {
        var house = new House
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Address = request.Address,
            ZipCode = request.ZipCode,
            City = request.City,
            UserId = userId,
            CreatedAt = DateTime.UtcNow
        };

        _context.Houses.Add(house);
        await _context.SaveChangesAsync();

        return new HouseDto(
            house.Id,
            house.Name,
            house.Address,
            house.ZipCode,
            house.City,
            house.CreatedAt
        );
    }

    public async Task<HouseDto?> UpdateHouseAsync(Guid houseId, UpdateHouseRequestDto request, Guid userId)
    {
        var house = await _context.Houses
            .FirstOrDefaultAsync(h => h.Id == houseId && h.UserId == userId);

        if (house == null)
        {
            return null;
        }

        if (request.Name != null) house.Name = request.Name;
        if (request.Address != null) house.Address = request.Address;
        if (request.ZipCode != null) house.ZipCode = request.ZipCode;
        if (request.City != null) house.City = request.City;
        house.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return new HouseDto(
            house.Id,
            house.Name,
            house.Address,
            house.ZipCode,
            house.City,
            house.CreatedAt
        );
    }

    public async Task<bool> DeleteHouseAsync(Guid houseId, Guid userId)
    {
        var house = await _context.Houses
            .FirstOrDefaultAsync(h => h.Id == houseId && h.UserId == userId);

        if (house == null)
        {
            return false;
        }

        _context.Houses.Remove(house);
        await _context.SaveChangesAsync();
        return true;
    }

    private HouseSummaryDto CalculateHouseSummary(House house)
    {
        var (score, pendingCount, overdueCount) = _calculator.CalculateHouseScore(house);

        return new HouseSummaryDto(
            house.Id,
            house.Name,
            house.Address,
            house.ZipCode,
            house.City,
            house.CreatedAt,
            score,
            house.Devices.Count,
            pendingCount,
            overdueCount
        );
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
}
