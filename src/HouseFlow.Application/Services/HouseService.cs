using HouseFlow.Application.Common;
using HouseFlow.Application.DTOs;
using HouseFlow.Application.Interfaces;
using HouseFlow.Core;
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
        // Houses the user owns or is a member of.
        var accessibleHouses = _context.Houses
            .AsNoTracking()
            .Where(h => h.UserId == userId || h.Members.Any(m => m.UserId == userId));

        // Flat projection: one row per (house, maintenance type), houses/devices with none kept via
        // DefaultIfEmpty. Only what the calculator needs is read — never the full entity graph — so this
        // is a single SQL statement (see issue #218).
        //
        // Role is resolved inline against `h` rather than by composing IHouseMemberService.ProjectHousesWithRole
        // into this query: EF Core cannot translate a further subquery (the devices/types flattening below)
        // correlated through a member of an already-`.Select()`-projected shape, even a plain scalar one — it
        // fails to push the correlation past that projection boundary. Composing it here would silently fall
        // back to the N+1 this rewrite exists to remove. The rule itself (ownership first, then membership)
        // mirrors HouseMemberService.GetUserRoleAsync exactly.
        var rows = await (
            from h in accessibleHouses
            from mt in (
                from d in _context.Devices
                where d.HouseId == h.Id
                from t in d.MaintenanceTypes
                select new
                {
                    t.Periodicity, t.CustomDays, t.CustomMonths, t.CreatedAt, t.BaselineDueDate,
                    LastDate = t.MaintenanceInstances.Max(i => (DateTime?)i.Date)
                }
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
                h.ColorKey,
                Role = h.UserId == userId
                    ? (HouseRole?)HouseRole.Owner
                    : h.Members.Where(m => m.UserId == userId).Select(m => (HouseRole?)m.Role).FirstOrDefault(),
                DeviceCount = h.Devices.Count,
                MembersCount = 1 + h.Members.Count(m => m.UserId != h.UserId),
                Periodicity = (Periodicity?)mt.Periodicity,
                mt.CustomDays,
                mt.CustomMonths,
                TypeCreatedAt = (DateTime?)mt.CreatedAt,
                mt.BaselineDueDate,
                LastMaintenanceDate = mt.LastDate
            }
        ).ToListAsync();

        // Device chips of the C4 cards: one light second statement (constant, not per house).
        var devicesByHouse = (await (
            from h in accessibleHouses
            from d in h.Devices
            select new { d.HouseId, d.Id, d.CreatedAt, d.Type }
        ).ToListAsync()).ToLookup(d => d.HouseId, d => (d.CreatedAt, d.Id, d.Type));

        var houseSummaries = rows
            .Where(r => r.Role != null)
            .GroupBy(r => r.Id)
            .Select(g =>
            {
                var first = g.First();
                var summary = _calculator.Summarize(g
                    .Where(r => r.Periodicity != null)
                    .Select(r => Snapshot(null, r.Periodicity!.Value, r.CustomDays, r.CustomMonths, r.TypeCreatedAt!.Value,
                        r.BaselineDueDate, r.LastMaintenanceDate)));

                return new HouseSummaryDto(
                    first.Id,
                    first.Name,
                    first.Address,
                    first.ZipCode,
                    first.City,
                    first.CreatedAt,
                    first.ColorKey,
                    summary.Score,
                    first.DeviceCount,
                    summary.Pending,
                    summary.Overdue,
                    first.Role!.Value.ToString(),
                    summary.Status,
                    summary.UpToDate,
                    summary.Total,
                    DeviceChips.TypesInCreationOrder(devicesByHouse[first.Id]),
                    first.MembersCount
                );
            })
            .ToList();

        var globalScore = houseSummaries.Count > 0
            ? (int)Math.Round(houseSummaries.Average(h => h.Score))
            : 100;

        // Owned houses are all in the list (the owner always has access), so the rotation needs no extra query.
        var nextColorKey = HouseColors.Next(houseSummaries
            .Where(h => h.UserRole == nameof(HouseRole.Owner))
            .Select(h => h.ColorKey));

        return new HousesListResponseDto(houseSummaries, globalScore, nextColorKey);
    }

    public async Task<HouseDetailDto?> GetHouseDetailAsync(Guid houseId, Guid userId)
    {
        var houseInfo = await _context.Houses
            .AsNoTracking()
            .Where(h => h.Id == houseId)
            .Select(h => new
            {
                h.Id, h.Name, h.Address, h.ZipCode, h.City, h.CreatedAt, h.ColorKey, DeviceCount = h.Devices.Count,
                MembersCount = 1 + h.Members.Count(m => m.UserId != h.UserId)
            })
            .FirstOrDefaultAsync();

        // 404 when the house does not exist, 403 when it exists but the caller is not a member
        // (same convention as devices and maintenance types).
        if (houseInfo == null) return null;

        var access = await _memberService.GetAccessInfoAsync(houseId, userId);
        _memberService.EnsureAccess(access, HousePermissions.Viewers);

        // Flat projection: one row per (device, maintenance type) of this house, single SQL statement.
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
                TypeName = t.Name,
                t.CustomDays,
                t.CustomMonths,
                TypeCreatedAt = (DateTime?)t.CreatedAt,
                t.BaselineDueDate,
                LastMaintenanceDate = t.MaintenanceInstances.Max(i => (DateTime?)i.Date)
            }
        ).ToListAsync();

        var snapshotsByDevice = deviceRows
            .GroupBy(r => r.Id)
            .Select(g => (Device: g.First(), Snapshots: g
                .Where(r => r.Periodicity != null)
                .Select(r => Snapshot(r.TypeName, r.Periodicity!.Value, r.CustomDays, r.CustomMonths,
                    r.TypeCreatedAt!.Value, r.BaselineDueDate, r.LastMaintenanceDate))
                .ToList()))
            .ToList();

        var deviceSummaries = snapshotsByDevice
            .Select(x =>
            {
                var summary = _calculator.Summarize(x.Snapshots);
                var next = _calculator.MostUrgent(x.Snapshots);
                return new DeviceSummaryDto(
                    x.Device.Id,
                    x.Device.Name,
                    x.Device.Type,
                    x.Device.Brand,
                    x.Device.Model,
                    x.Device.InstallDate,
                    houseId,
                    x.Device.CreatedAt,
                    summary.Score,
                    summary.Status,
                    summary.Pending,
                    x.Device.MaintenanceTypesCount,
                    summary.Overdue,
                    summary.UpToDate,
                    next?.NextDueDate,
                    next?.Name
                );
            })
            .ToList();

        var houseSummary = _calculator.Summarize(snapshotsByDevice.SelectMany(x => x.Snapshots));

        return new HouseDetailDto(
            houseInfo.Id,
            houseInfo.Name,
            houseInfo.Address,
            houseInfo.ZipCode,
            houseInfo.City,
            houseInfo.CreatedAt,
            houseInfo.ColorKey,
            houseSummary.Score,
            houseInfo.DeviceCount,
            houseSummary.Pending,
            houseSummary.Overdue,
            deviceSummaries,
            access.Role!.Value.ToString(),
            houseSummary.Status,
            houseSummary.UpToDate,
            houseSummary.Total,
            HousePermissions.Capabilities(access),
            DeviceChips.TypesInCreationOrder(deviceSummaries.Select(d => (d.CreatedAt, d.Id, d.Type))),
            houseInfo.MembersCount
        );
    }

    /// <summary>
    /// Snapshot of a projected row. Id/DeviceId are irrelevant to status calculation and left at their defaults;
    /// the name feeds the device's most urgent maintenance (C4 subtitle).
    /// </summary>
    private static MaintenanceTypeSnapshot Snapshot(
        string? name, Periodicity periodicity, int? customDays, int? customMonths, DateTime createdAt,
        DateTime? baselineDueDate, DateTime? lastMaintenanceDate) =>
        new(Guid.Empty, name ?? string.Empty, periodicity, customDays, customMonths, Guid.Empty, createdAt,
            baselineDueDate, lastMaintenanceDate);

    public async Task<HouseDto> CreateHouseAsync(CreateHouseRequestDto request, Guid userId)
    {
        var colorKey = request.ColorKey is { } requested
            ? HouseColorKeys.ToKey(requested)
            : HouseColors.Next(await _context.Houses
                .Where(h => h.UserId == userId)
                .Select(h => h.ColorKey)
                .ToListAsync());

        var house = new House
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Address = request.Address,
            ZipCode = request.ZipCode,
            City = request.City,
            ColorKey = colorKey,
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
            house.CreatedAt,
            house.ColorKey
        );
    }

    public async Task<HouseDto?> UpdateHouseAsync(Guid houseId, UpdateHouseRequestDto request, Guid userId)
    {
        var house = await _context.Houses.FirstOrDefaultAsync(h => h.Id == houseId);
        if (house == null) return null;

        // Only owner can update house
        await _memberService.EnsureAccessAsync(houseId, userId, HousePermissions.Owners);

        if (request.Name != null) house.Name = request.Name;
        if (request.Address != null) house.Address = request.Address;
        if (request.ZipCode != null) house.ZipCode = request.ZipCode;
        if (request.City != null) house.City = request.City;
        if (request.ColorKey is { } colorKey) house.ColorKey = HouseColorKeys.ToKey(colorKey);
        house.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return new HouseDto(
            house.Id,
            house.Name,
            house.Address,
            house.ZipCode,
            house.City,
            house.CreatedAt,
            house.ColorKey
        );
    }

    public async Task<bool> DeleteHouseAsync(Guid houseId, Guid userId)
    {
        var house = await _context.Houses.FirstOrDefaultAsync(h => h.Id == houseId);
        if (house == null) return false;

        // Only owner can delete house
        await _memberService.EnsureAccessAsync(houseId, userId, HousePermissions.Owners);

        _context.Houses.Remove(house);
        await _context.SaveChangesAsync();
        return true;
    }

}
