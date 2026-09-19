using HouseFlow.Application.Common;
using HouseFlow.Application.DTOs;
using HouseFlow.Application.Interfaces;
using HouseFlow.Core.Entities;
using HouseFlow.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace HouseFlow.Application.Services;

public class DeviceService : IDeviceService
{
    private readonly IApplicationDbContext _context;
    private readonly IMaintenanceCalculatorService _calculator;
    private readonly IHouseMemberService _memberService;

    public DeviceService(IApplicationDbContext context, IMaintenanceCalculatorService calculator, IHouseMemberService memberService)
    {
        _context = context;
        _calculator = calculator;
        _memberService = memberService;
    }

    public async Task<IEnumerable<DeviceSummaryDto>> GetHouseDevicesAsync(Guid houseId, Guid userId)
    {
        // Any member can view devices
        await _memberService.EnsureAccessAsync(houseId, userId,
            HouseRole.Owner, HouseRole.CollaboratorRW, HouseRole.CollaboratorRO, HouseRole.Tenant);

        // Flat projection: one row per (device, maintenance type), single SQL statement instead of the
        // previous Include().ThenInclude() split-query chain materializing every instance.
        var rows = await (
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

        return rows
            .GroupBy(r => r.Id)
            .Select(g =>
            {
                var first = g.First();
                var snapshots = g
                    .Where(r => r.Periodicity != null)
                    .Select(r => new MaintenanceTypeSnapshot(
                        Guid.Empty, string.Empty, r.Periodicity!.Value, r.CustomDays, Guid.Empty, default, r.LastMaintenanceDate))
                    .ToList();
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
    }

    public async Task<DeviceDetailDto?> GetDeviceDetailAsync(Guid deviceId, Guid userId)
    {
        // Device-level scalars and aggregates, one row, computed once by PostgreSQL — sum(Cost)/count(*)
        // across all instances instead of loading every instance to sum them in memory. Kept out of the
        // per-type projection below: a correlated subquery in a SELECT list runs once per output row, so
        // folding these into the (fan-out) per-type query would recompute the same device-wide aggregate
        // once per maintenance type instead of once per device.
        var device = await _context.Devices
            .AsNoTracking()
            .Where(d => d.Id == deviceId)
            .Select(d => new
            {
                d.Id,
                d.Name,
                d.Type,
                d.Brand,
                d.Model,
                d.InstallDate,
                d.HouseId,
                d.CreatedAt,
                MaintenanceTypesCount = d.MaintenanceTypes.Count,
                TotalSpent = d.MaintenanceTypes.SelectMany(mt => mt.MaintenanceInstances).Sum(i => (decimal?)i.Cost),
                HasAnyCost = d.MaintenanceTypes.SelectMany(mt => mt.MaintenanceInstances).Any(i => i.Cost != null),
                MaintenanceCount = d.MaintenanceTypes.SelectMany(mt => mt.MaintenanceInstances).Count()
            })
            .FirstOrDefaultAsync();

        if (device == null) return null;

        // Any member can view devices. Role and cost-visibility are resolved from the same access row
        // instead of two separate calls each re-querying the house membership (H1).
        var access = await _memberService.GetAccessInfoAsync(device.HouseId, userId);
        _memberService.EnsureAccess(access, HouseRole.Owner, HouseRole.CollaboratorRW, HouseRole.CollaboratorRO, HouseRole.Tenant);
        var hideCosts = _memberService.ShouldHideCosts(access);

        // Flat projection: one row per maintenance type, only what CalculateDeviceScore/CalculateMaintenanceTypeWithStatus need.
        var typeRows = await (
            from t in _context.MaintenanceTypes
            where t.DeviceId == deviceId
            orderby t.Id
            select new
            {
                t.Id,
                t.Name,
                t.Periodicity,
                t.CustomDays,
                t.CreatedAt,
                LastMaintenanceDate = t.MaintenanceInstances.Max(i => (DateTime?)i.Date)
            }
        ).ToListAsync();

        var snapshots = typeRows
            .Select(r => new MaintenanceTypeSnapshot(r.Id, r.Name, r.Periodicity, r.CustomDays, deviceId, r.CreatedAt, r.LastMaintenanceDate))
            .ToList();

        var (score, status, pendingCount) = _calculator.CalculateDeviceScore(snapshots);
        var maintenanceTypes = snapshots.Select(s => _calculator.CalculateMaintenanceTypeWithStatus(s)).ToList();
        // When no instance has a non-null cost, Postgres's SUM() has nothing to add and the Npgsql provider
        // COALESCEs it to the literal 0.0 (scale 1), whereas the in-memory LINQ Sum() this replaces yields a
        // scale-0 0m for the same case; System.Text.Json serializes those differently ("0.0" vs "0"). Use a
        // real scale-0 zero for that case instead of trusting the SQL-side literal (see #218).
        var totalSpent = hideCosts || !device.HasAnyCost ? 0m : device.TotalSpent!.Value;

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
            device.MaintenanceTypesCount,
            maintenanceTypes,
            totalSpent,
            device.MaintenanceCount
        );
    }

    public async Task<DeviceDto> CreateDeviceAsync(Guid houseId, CreateDeviceRequestDto request, Guid userId)
    {
        // Owner and CollaboratorRW can create devices
        await _memberService.EnsureAccessAsync(houseId, userId, HouseRole.Owner, HouseRole.CollaboratorRW);

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
        var device = await _context.Devices.FirstOrDefaultAsync(d => d.Id == deviceId);
        if (device == null) return null;

        // Owner and CollaboratorRW can update devices
        await _memberService.EnsureAccessAsync(device.HouseId, userId, HouseRole.Owner, HouseRole.CollaboratorRW);

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
        var device = await _context.Devices.FirstOrDefaultAsync(d => d.Id == deviceId);
        if (device == null) return false;

        // Owner and CollaboratorRW can delete devices
        await _memberService.EnsureAccessAsync(device.HouseId, userId, HouseRole.Owner, HouseRole.CollaboratorRW);

        _context.Devices.Remove(device);
        await _context.SaveChangesAsync();
        return true;
    }

}
