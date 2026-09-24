using HouseFlow.Application.Common;
using HouseFlow.Application.DTOs;
using HouseFlow.Application.Interfaces;
using HouseFlow.Core.Entities;
using HouseFlow.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace HouseFlow.Application.Services;

public class HouseService : IHouseService
{
    private readonly IApplicationDbContext _context;
    private readonly IMaintenanceCalculatorService _calculator;
    private readonly IHouseMemberService _memberService;

    public HouseService(IApplicationDbContext context, IMaintenanceCalculatorService calculator, IHouseMemberService memberService)
    {
        _context = context;
        _calculator = calculator;
        _memberService = memberService;
    }

    public async Task<HousesListResponseDto> GetUserHousesAsync(Guid userId)
    {
        // Houses the user owns or is a member of. The role for each house is resolved by
        // IHouseMemberService.ProjectHousesWithRole (one correlated subquery per house, composed into
        // this same query) instead of an N+1 loop calling GetUserRoleAsync per house.
        var accessibleHouses = _context.Houses
            .AsNoTracking()
            .Where(h => h.UserId == userId || h.Members.Any(m => m.UserId == userId));

        // Flat projection: one row per (house, maintenance type), houses/devices with none kept via
        // DefaultIfEmpty. Only periodicity/customDays/last instance date are read — never the full
        // entity graph — so this is a single SQL statement instead of the previous 10 (see issue #218).
        //
        // Role is resolved inline against `h` rather than by composing IHouseMemberService.ProjectHousesWithRole
        // into this query: EF Core cannot translate a further subquery (the devices/types flattening below)
        // correlated through a member of an already-`.Select()`-projected shape, even a plain scalar one — it
        // fails to push the correlation past that projection boundary. Composing it here would silently fall
        // back to the N+1 this rewrite exists to remove. The rule itself (ownership first, then membership)
        // mirrors HouseMemberService.GetUserRoleAsync exactly; ProjectHousesWithRole remains available for
        // callers that don't need to flatten further.
        var rows = await (
            from h in accessibleHouses
            from mt in (
                from d in _context.Devices
                where d.HouseId == h.Id
                from t in d.MaintenanceTypes
                select new { t.Periodicity, t.CustomDays, LastDate = t.MaintenanceInstances.Max(i => (DateTime?)i.Date) }
            ).DefaultIfEmpty()
            orderby h.Id
            select new
            {
                h.Id,
                h.Name,
                h.Address,
                h.ZipCode,
                h.City,
                h.CreatedAt,
                Role = h.UserId == userId
                    ? (HouseRole?)HouseRole.Owner
                    : h.Members.Where(m => m.UserId == userId).Select(m => (HouseRole?)m.Role).FirstOrDefault(),
                DeviceCount = h.Devices.Count,
                Periodicity = (Periodicity?)mt.Periodicity,
                mt.CustomDays,
                LastMaintenanceDate = mt.LastDate
            }
        ).ToListAsync();

        var houseSummaries = rows
            .Where(r => r.Role != null)
            .GroupBy(r => r.Id)
            .Select(g =>
            {
                var first = g.First();
                var snapshots = ToSnapshots(g, r => r.Periodicity, r => r.CustomDays, r => r.LastMaintenanceDate);
                var (score, pendingCount, overdueCount) = _calculator.CalculateHouseScore(snapshots);

                return new HouseSummaryDto(
                    first.Id,
                    first.Name,
                    first.Address,
                    first.ZipCode,
                    first.City,
                    first.CreatedAt,
                    score,
                    first.DeviceCount,
                    pendingCount,
                    overdueCount,
                    first.Role!.Value.ToString()
                );
            })
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

        var houseInfo = await _context.Houses
            .AsNoTracking()
            .Where(h => h.Id == houseId)
            .Select(h => new { h.Id, h.Name, h.Address, h.ZipCode, h.City, h.CreatedAt, DeviceCount = h.Devices.Count })
            .FirstOrDefaultAsync();

        if (houseInfo == null) return null;

        // Flat projection: one row per (device, maintenance type) of this house, single SQL statement
        // (down from the 3 split-query statements the previous Include().ThenInclude() chain issued).
        var deviceRows = await (
            from d in _context.Devices
            where d.HouseId == houseId
            from t in d.MaintenanceTypes.DefaultIfEmpty()
            orderby d.Id
            select new
            {
                d.Id,
                d.Name,
                d.Type,
                d.Brand,
                d.Model,
                d.InstallDate,
                d.CreatedAt,
                MaintenanceTypesCount = d.MaintenanceTypes.Count,
                Periodicity = (Periodicity?)t.Periodicity,
                t.CustomDays,
                LastMaintenanceDate = t.MaintenanceInstances.Max(i => (DateTime?)i.Date)
            }
        ).ToListAsync();

        var deviceSummaries = deviceRows
            .GroupBy(r => r.Id)
            .Select(g =>
            {
                var first = g.First();
                var snapshots = ToSnapshots(g, r => r.Periodicity, r => r.CustomDays, r => r.LastMaintenanceDate);
                var (score, status, pendingCount) = _calculator.CalculateDeviceScore(snapshots);

                return new DeviceSummaryDto(
                    first.Id,
                    first.Name,
                    first.Type,
                    first.Brand,
                    first.Model,
                    first.InstallDate,
                    houseId,
                    first.CreatedAt,
                    score,
                    status,
                    pendingCount,
                    first.MaintenanceTypesCount
                );
            })
            .ToList();

        var allSnapshots = ToSnapshots(deviceRows, r => r.Periodicity, r => r.CustomDays, r => r.LastMaintenanceDate);
        var (houseScore, housePendingCount, houseOverdueCount) = _calculator.CalculateHouseScore(allSnapshots);

        return new HouseDetailDto(
            houseInfo.Id,
            houseInfo.Name,
            houseInfo.Address,
            houseInfo.ZipCode,
            houseInfo.City,
            houseInfo.CreatedAt,
            houseScore,
            houseInfo.DeviceCount,
            housePendingCount,
            houseOverdueCount,
            deviceSummaries,
            role.Value.ToString()
        );
    }

    /// <summary>
    /// Turns flat (periodicity, customDays, lastMaintenanceDate) rows into the snapshots
    /// <see cref="IMaintenanceCalculatorService"/> needs, dropping the placeholder row a house/device with no
    /// maintenance types produces (periodicity null). Id/Name/DeviceId/CreatedAt are irrelevant to score
    /// calculation and left at their defaults.
    /// </summary>
    private static List<MaintenanceTypeSnapshot> ToSnapshots<TRow>(
        IEnumerable<TRow> rows,
        Func<TRow, Periodicity?> periodicity,
        Func<TRow, int?> customDays,
        Func<TRow, DateTime?> lastMaintenanceDate)
    {
        return rows
            .Where(r => periodicity(r) != null)
            .Select(r => new MaintenanceTypeSnapshot(
                Guid.Empty, string.Empty, periodicity(r)!.Value, customDays(r), Guid.Empty, default, lastMaintenanceDate(r)))
            .ToList();
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

}
