using HouseFlow.Application.DTOs;
using HouseFlow.Application.Interfaces;
using HouseFlow.Core.Entities;
using HouseFlow.Core.Enums;
using HouseFlow.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HouseFlow.Infrastructure.Services;

public class HouseService : IHouseService
{
    private readonly HouseFlowDbContext _context;
    private readonly IMaintenanceCalculatorService _calculator;
    private readonly IHouseMemberService _memberService;

    public HouseService(HouseFlowDbContext context, IMaintenanceCalculatorService calculator, IHouseMemberService memberService)
    {
        _context = context;
        _calculator = calculator;
        _memberService = memberService;
    }

    public async Task<HousesListResponseDto> GetUserHousesAsync(Guid userId)
    {
        // Get houses the user owns
        var ownedHouseIds = await _context.Houses
            .AsNoTracking()
            .Where(h => h.UserId == userId)
            .Select(h => h.Id)
            .ToListAsync();

        // Get houses the user is a member of
        var memberHouseIds = await _context.HouseMembers
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => m.HouseId)
            .ToListAsync();

        var allHouseIds = ownedHouseIds.Union(memberHouseIds).Distinct().ToList();

        var houses = await _context.Houses
            .AsNoTracking()
            .Where(h => allHouseIds.Contains(h.Id))
            .Include(h => h.Devices)
                .ThenInclude(d => d.MaintenanceTypes)
                    .ThenInclude(mt => mt.MaintenanceInstances)
            .ToListAsync();

        // Determine user's role for each house to decide if costs should be hidden
        var userRole = new Dictionary<Guid, HouseRole>();
        foreach (var houseId in allHouseIds)
        {
            var role = await _memberService.GetUserRoleAsync(houseId, userId);
            if (role != null) userRole[houseId] = role.Value;
        }

        var houseSummaries = houses
            .Where(h => userRole.ContainsKey(h.Id))
            .Select(h => CalculateHouseSummary(h, userRole[h.Id]))
            .ToList();

        var globalScore = houseSummaries.Count > 0
            ? (int)Math.Round(houseSummaries.Average(h => h.Score))
            : 100;

        return new HousesListResponseDto(houseSummaries, globalScore);
    }

    public async Task<HouseDetailDto?> GetHouseDetailAsync(Guid houseId, Guid userId)
    {
        var role = await _memberService.GetUserRoleAsync(houseId, userId);
        if (role == null) return null;

        var house = await _context.Houses
            .AsNoTracking()
            .Where(h => h.Id == houseId)
            .Include(h => h.Devices)
                .ThenInclude(d => d.MaintenanceTypes)
                    .ThenInclude(mt => mt.MaintenanceInstances)
            .FirstOrDefaultAsync();

        if (house == null) return null;

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
            deviceSummaries,
            role.Value.ToString()
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

        // Also create Owner membership
        var member = new HouseMember
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            HouseId = house.Id,
            Role = HouseRole.Owner,
            CanLogMaintenance = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.HouseMembers.Add(member);

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
        var house = await _context.Houses.FirstOrDefaultAsync(h => h.Id == houseId);
        if (house == null) return null;

        // Only owner can update house
        await _memberService.EnsureAccessAsync(houseId, userId, HouseRole.Owner);

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
        var house = await _context.Houses.FirstOrDefaultAsync(h => h.Id == houseId);
        if (house == null) return false;

        // Only owner can delete house
        await _memberService.EnsureAccessAsync(houseId, userId, HouseRole.Owner);

        _context.Houses.Remove(house);
        await _context.SaveChangesAsync();
        return true;
    }

    private HouseSummaryDto CalculateHouseSummary(House house, HouseRole role)
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
            overdueCount,
            role.ToString()
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
