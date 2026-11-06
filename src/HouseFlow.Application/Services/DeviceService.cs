using HouseFlow.Application.Common;
using HouseFlow.Application.DTOs;
using HouseFlow.Application.Interfaces;
using HouseFlow.Core.Entities;
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
        await _memberService.EnsureAccessAsync(houseId, userId, HousePermissions.Viewers);

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
                TypeName = t.Name,
                t.CustomDays,
                t.CustomMonths,
                TypeCreatedAt = (DateTime?)t.CreatedAt,
                t.BaselineDueDate,
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
                        Guid.Empty, r.TypeName ?? string.Empty, r.Periodicity!.Value, r.CustomDays, r.CustomMonths, Guid.Empty,
                        r.TypeCreatedAt!.Value, r.BaselineDueDate, r.LastMaintenanceDate))
                    .ToList();
                var summary = _calculator.Summarize(snapshots);
                var next = _calculator.MostUrgent(snapshots);

                return new DeviceSummaryDto(
                    first.Id,
                    first.Name,
                    first.Type,
                    first.Brand,
                    first.Model,
                    first.InstallDate,
                    houseId,
                    first.CreatedAt,
                    summary.Score,
                    summary.Status,
                    summary.Pending,
                    first.MaintenanceTypesCount,
                    summary.Overdue,
                    summary.UpToDate,
                    next?.NextDueDate,
                    next?.Name
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
                HouseName = d.House!.Name,
                d.CreatedAt,
                MaintenanceTypesCount = d.MaintenanceTypes.Count,
                TotalSpent = d.MaintenanceTypes.SelectMany(mt => mt.MaintenanceInstances).Sum(i => (decimal?)i.Cost),
                HasAnyCost = d.MaintenanceTypes.SelectMany(mt => mt.MaintenanceInstances).Any(i => i.Cost != null),
                MaintenanceCount = d.MaintenanceTypes.SelectMany(mt => mt.MaintenanceInstances).Count()
            })
            .FirstOrDefaultAsync();

        if (device == null) return null;

        // Any member can view devices. Role, cost visibility and logging right are resolved from the same
        // access row instead of separate calls each re-querying the house membership (H1).
        var access = await _memberService.GetAccessInfoAsync(device.HouseId, userId);
        _memberService.EnsureAccess(access, HousePermissions.Viewers);
        var hideCosts = _memberService.ShouldHideCosts(access);

        // Flat projection: one row per maintenance type, only what the calculator needs.
        var typeRows = await (
            from t in _context.MaintenanceTypes
            where t.DeviceId == deviceId
            orderby t.CreatedAt, t.Id
            select new
            {
                t.Id,
                t.Name,
                t.Periodicity,
                t.CustomDays,
                t.CustomMonths,
                t.CreatedAt,
                t.BaselineDueDate,
                LastMaintenanceDate = t.MaintenanceInstances.Max(i => (DateTime?)i.Date)
            }
        ).ToListAsync();

        var snapshots = typeRows
            .Select(r => new MaintenanceTypeSnapshot(r.Id, r.Name, r.Periodicity, r.CustomDays, r.CustomMonths, deviceId,
                r.CreatedAt, r.BaselineDueDate, r.LastMaintenanceDate))
            .ToList();

        var summary = _calculator.Summarize(snapshots);
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
            summary.Score,
            summary.Status,
            summary.Pending,
            device.MaintenanceTypesCount,
            maintenanceTypes,
            totalSpent,
            device.MaintenanceCount,
            summary.Overdue,
            summary.UpToDate,
            device.HouseName,
            access.Role!.Value.ToString(),
            HousePermissions.Capabilities(access)
        );
    }

    public async Task<DeviceDto> CreateDeviceAsync(Guid houseId, CreateDeviceRequestDto request, Guid userId)
    {
        await _memberService.EnsureAccessAsync(houseId, userId, HousePermissions.Editors);

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

        // Catalogue type (M2 / P06): its default maintenance type is created in the same SaveChanges,
        // so a failure on the type leaves no orphan device behind.
        if (request.MaintenanceType is { } maintenanceType)
        {
            var (type, record) = MaintenanceTypeFactory.Create(
                device.Id, MaintenanceTypeFactory.FromDeviceRequest(maintenanceType), _calculator);
            _context.MaintenanceTypes.Add(type);
            if (record != null) _context.MaintenanceInstances.Add(record);
        }

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

        await _memberService.EnsureAccessAsync(device.HouseId, userId, HousePermissions.Editors);

        // Replacement semantics: the optional fields take the value sent (null clears them); the
        // required name and type are kept when omitted.
        if (request.Name != null) device.Name = request.Name;
        if (request.Type != null) device.Type = request.Type;
        device.Brand = string.IsNullOrWhiteSpace(request.Brand) ? null : request.Brand;
        device.Model = string.IsNullOrWhiteSpace(request.Model) ? null : request.Model;
        device.InstallDate = request.InstallDate;
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

        await _memberService.EnsureAccessAsync(device.HouseId, userId, HousePermissions.Editors);

        _context.Devices.Remove(device);
        await _context.SaveChangesAsync();
        return true;
    }

}
