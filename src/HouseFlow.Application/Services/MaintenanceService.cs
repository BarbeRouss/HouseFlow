using HouseFlow.Application.Common;
using HouseFlow.Application.DTOs;
using HouseFlow.Application.Interfaces;
using HouseFlow.Core.Entities;
using HouseFlow.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace HouseFlow.Application.Services;

public class MaintenanceService : IMaintenanceService
{
    private readonly IApplicationDbContext _context;
    private readonly IMaintenanceCalculatorService _calculator;
    private readonly IHouseMemberService _memberService;

    public MaintenanceService(IApplicationDbContext context, IMaintenanceCalculatorService calculator, IHouseMemberService memberService)
    {
        _context = context;
        _calculator = calculator;
        _memberService = memberService;
    }

    public async Task<IEnumerable<MaintenanceTypeWithStatusDto>> GetDeviceMaintenanceTypesAsync(Guid deviceId, Guid userId)
    {
        var houseId = await _context.Devices
            .Where(d => d.Id == deviceId)
            .Select(d => (Guid?)d.HouseId)
            .FirstOrDefaultAsync()
            ?? throw new KeyNotFoundException("Device not found");

        await _memberService.EnsureAccessAsync(houseId, userId, HousePermissions.Viewers);

        var snapshots = await _context.MaintenanceTypes
            .AsNoTracking()
            .Where(t => t.DeviceId == deviceId)
            .OrderBy(t => t.CreatedAt).ThenBy(t => t.Id)
            .Select(t => new MaintenanceTypeSnapshot(
                t.Id, t.Name, t.Periodicity, t.CustomDays, t.CustomMonths, t.DeviceId, t.CreatedAt, t.BaselineDueDate,
                t.MaintenanceInstances.Max(i => (DateTime?)i.Date)))
            .ToListAsync();

        return snapshots.Select(_calculator.CalculateMaintenanceTypeWithStatus).ToList();
    }

    public async Task<MaintenanceTypeDto> CreateMaintenanceTypeAsync(Guid deviceId, CreateMaintenanceTypeRequestDto request, Guid userId)
    {
        var device = await _context.Devices.FirstOrDefaultAsync(d => d.Id == deviceId);
        if (device == null) throw new KeyNotFoundException("Device not found");

        await _memberService.EnsureAccessAsync(device.HouseId, userId, HousePermissions.Editors);

        var (maintenanceType, record) = MaintenanceTypeFactory.Create(deviceId, request, _calculator);

        _context.MaintenanceTypes.Add(maintenanceType);
        if (record != null) _context.MaintenanceInstances.Add(record);
        await _context.SaveChangesAsync();

        return ToDto(maintenanceType);
    }

    public async Task<MaintenanceTypeDto?> UpdateMaintenanceTypeAsync(Guid typeId, UpdateMaintenanceTypeRequestDto request, Guid userId)
    {
        var maintenanceType = await _context.MaintenanceTypes
            .Include(mt => mt.Device)
            .FirstOrDefaultAsync(mt => mt.Id == typeId);

        if (maintenanceType?.Device == null) return null;

        await _memberService.EnsureAccessAsync(maintenanceType.Device.HouseId, userId, HousePermissions.Editors);

        // A new interval in the request replaces the stored one (months win over days when both are sent).
        var periodicity = request.Periodicity ?? maintenanceType.Periodicity;
        var (customDays, customMonths) = request.CustomMonths is not null || request.CustomDays is not null
            ? (request.CustomMonths is null ? request.CustomDays : null, request.CustomMonths)
            : (maintenanceType.CustomDays, maintenanceType.CustomMonths);
        (customDays, customMonths) = MaintenanceTypeFactory.NormalizeCustomInterval(periodicity, customDays, customMonths);

        if (request.Name != null) maintenanceType.Name = request.Name;
        maintenanceType.Periodicity = periodicity;
        maintenanceType.CustomDays = customDays;
        maintenanceType.CustomMonths = customMonths;
        maintenanceType.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        // The next due date is derived on read from the last record: changing the periodicity recalculates it (M4).
        return ToDto(maintenanceType);
    }

    public async Task<bool> DeleteMaintenanceTypeAsync(Guid typeId, Guid userId)
    {
        var maintenanceType = await _context.MaintenanceTypes
            .Include(mt => mt.Device)
            .FirstOrDefaultAsync(mt => mt.Id == typeId);

        if (maintenanceType?.Device == null) return false;

        await _memberService.EnsureAccessAsync(maintenanceType.Device.HouseId, userId, HousePermissions.Editors);

        _context.MaintenanceTypes.Remove(maintenanceType);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<MaintenanceInstanceDto> LogMaintenanceAsync(Guid typeId, LogMaintenanceRequestDto request, Guid userId)
    {
        var maintenanceType = await _context.MaintenanceTypes
            .Include(mt => mt.Device)
            .FirstOrDefaultAsync(mt => mt.Id == typeId);

        if (maintenanceType?.Device == null) throw new KeyNotFoundException("Maintenance type not found");

        var access = await EnsureCanLogAsync(maintenanceType.Device.HouseId, userId);

        if (NotInFutureAttribute.IsInFuture(request.Date))
            throw new InvalidOperationException("Maintenance date cannot be in the future");

        // Same rule as the edit: a caller who cannot see costs (tenant without canViewCosts) has
        // no say on them either — what they send is ignored, never stored nor echoed back.
        var canViewCosts = HousePermissions.CanViewCosts(access);

        var instance = new MaintenanceInstance
        {
            Id = Guid.NewGuid(),
            // A calendar day, carried as its UTC midnight like every read (…T00:00:00Z).
            Date = ParisClock.AsDate(request.Date),
            Cost = canViewCosts ? request.Cost : null,
            Provider = canViewCosts ? NullIfBlank(request.Provider) : null,
            Notes = request.Notes,
            MaintenanceTypeId = typeId,
            CreatedAt = DateTime.UtcNow
        };

        _context.MaintenanceInstances.Add(instance);
        await _context.SaveChangesAsync();

        return ToDto(instance, maintenanceType.Name, hideCosts: !canViewCosts);
    }

    public async Task<MaintenanceHistoryResponseDto> GetDeviceMaintenanceHistoryAsync(Guid deviceId, Guid userId)
    {
        var device = await _context.Devices
            .AsNoTracking()
            .Include(d => d.MaintenanceTypes)
                .ThenInclude(mt => mt.MaintenanceInstances)
            .FirstOrDefaultAsync(d => d.Id == deviceId);

        if (device == null) throw new KeyNotFoundException("Device not found");

        var access = await _memberService.GetAccessInfoAsync(device.HouseId, userId);
        _memberService.EnsureAccess(access, HousePermissions.Viewers);

        // Hide costs if tenant without canViewCosts permission
        var hideCosts = _memberService.ShouldHideCosts(access);

        var instances = device.MaintenanceTypes
            .SelectMany(mt => mt.MaintenanceInstances.Select(i => new MaintenanceInstanceDto(
                i.Id,
                i.Date,
                hideCosts ? null : i.Cost,
                hideCosts ? null : i.Provider,
                i.Notes,
                i.MaintenanceTypeId,
                mt.Name,
                i.CreatedAt
            )))
            .OrderByDescending(i => i.Date)
            .ToList();

        var totalSpent = hideCosts ? 0 : instances.Sum(i => i.Cost ?? 0);

        return new MaintenanceHistoryResponseDto(instances, totalSpent, instances.Count);
    }

    public async Task<MaintenanceInstanceDto?> UpdateMaintenanceInstanceAsync(Guid instanceId, UpdateMaintenanceInstanceRequestDto request, Guid userId)
    {
        var instance = await _context.MaintenanceInstances
            .Include(i => i.MaintenanceType)
                .ThenInclude(mt => mt!.Device)
            .FirstOrDefaultAsync(i => i.Id == instanceId);

        if (instance?.MaintenanceType?.Device == null) return null;

        // R5: whoever may log a record may also edit one (tenant included).
        var access = await EnsureCanLogAsync(instance.MaintenanceType.Device.HouseId, userId);

        // Replacement semantics: the optional fields take the value sent (null clears them); the
        // required date is kept when omitted.
        if (request.Date != null)
        {
            if (NotInFutureAttribute.IsInFuture(request.Date.Value))
                throw new InvalidOperationException("Maintenance date cannot be in the future");
            instance.Date = ParisClock.AsDate(request.Date.Value);
        }
        // A caller who cannot see costs (tenant without canViewCosts) never received them: keep them.
        var canViewCosts = HousePermissions.CanViewCosts(access);
        if (canViewCosts)
        {
            instance.Cost = request.Cost;
            instance.Provider = NullIfBlank(request.Provider);
        }
        instance.Notes = NullIfBlank(request.Notes);
        instance.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return ToDto(instance, instance.MaintenanceType.Name, hideCosts: !canViewCosts);
    }

    public async Task<bool> DeleteMaintenanceInstanceAsync(Guid instanceId, Guid userId)
    {
        var instance = await _context.MaintenanceInstances
            .Include(i => i.MaintenanceType)
                .ThenInclude(mt => mt!.Device)
            .FirstOrDefaultAsync(i => i.Id == instanceId);

        if (instance?.MaintenanceType?.Device == null) return false;

        // R5: deleting a record is for the owner and RW collaborators, not tenants.
        await _memberService.EnsureAccessAsync(instance.MaintenanceType.Device.HouseId, userId, HousePermissions.Editors);

        // R2: nothing to recompute here — the next due date is derived on read from the latest
        // remaining record, or from the type's no-history baseline once none is left.
        _context.MaintenanceInstances.Remove(instance);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<UpcomingTasksResponseDto> GetUpcomingTasksAsync(Guid userId, int? limit = null)
    {
        var tasks = await GetAllTasksAsync(userId);
        var toHandle = tasks.Where(IsToHandle).ToList();

        var overdueCount = toHandle.Count(t => t.Status == MaintenanceStatuses.Overdue);
        var pendingCount = toHandle.Count - overdueCount;
        var result = limit.HasValue ? toHandle.Take(limit.Value).ToList() : toHandle;

        return new UpcomingTasksResponseDto(result, overdueCount, pendingCount);
    }

    public async Task<DashboardDto> GetDashboardAsync(Guid userId)
    {
        var tasks = await GetAllTasksAsync(userId);
        var toHandle = tasks.Where(IsToHandle).ToList();
        var overdueCount = toHandle.Count(t => t.Status == MaintenanceStatuses.Overdue);

        return new DashboardDto(
            toHandle,
            toHandle.Count,
            overdueCount,
            toHandle.Count - overdueCount,
            tasks.Count - toHandle.Count,
            tasks.Count,
            tasks.FirstOrDefault(t => t.Status == MaintenanceStatuses.UpToDate));
    }

    private static bool IsToHandle(UpcomingTaskDto task) =>
        task.Status is MaintenanceStatuses.Overdue or MaintenanceStatuses.Pending;

    /// <summary>
    /// Every maintenance type of every house the user can see, with its R1 status, sorted by next due
    /// date (overdue ones come first by construction), then by name. One SQL statement.
    /// </summary>
    private async Task<List<UpcomingTaskDto>> GetAllTasksAsync(Guid userId)
    {
        var rows = await (
            from t in _context.MaintenanceTypes.AsNoTracking()
            let d = t.Device!
            let h = d.House!
            where h.UserId == userId || h.Members.Any(m => m.UserId == userId)
            select new
            {
                t.Id,
                t.Name,
                t.Periodicity,
                t.CustomDays,
                t.CustomMonths,
                t.CreatedAt,
                t.BaselineDueDate,
                LastMaintenanceDate = t.MaintenanceInstances.Max(i => (DateTime?)i.Date),
                DeviceId = d.Id,
                DeviceName = d.Name,
                DeviceType = d.Type,
                HouseId = h.Id,
                HouseName = h.Name,
                IsOwner = h.UserId == userId,
                MemberRole = h.Members.Where(m => m.UserId == userId).Select(m => (HouseRole?)m.Role).FirstOrDefault(),
                MemberCanLog = h.Members.Where(m => m.UserId == userId).Select(m => (bool?)m.CanLogMaintenance).FirstOrDefault(),
                MemberCanViewCosts = h.Members.Where(m => m.UserId == userId).Select(m => (bool?)m.CanViewCosts).FirstOrDefault()
            }
        ).ToListAsync();

        var today = _calculator.Today;

        return rows
            .Select(r =>
            {
                var snapshot = new MaintenanceTypeSnapshot(r.Id, r.Name, r.Periodicity, r.CustomDays, r.CustomMonths,
                    r.DeviceId, r.CreatedAt, r.BaselineDueDate, r.LastMaintenanceDate);
                var nextDueDate = _calculator.CalculateNextDueDate(snapshot);
                var access = r.IsOwner
                    ? new HouseAccessInfo(HouseRole.Owner, true, true)
                    : new HouseAccessInfo(r.MemberRole, r.MemberCanViewCosts ?? false, r.MemberCanLog ?? false);

                return new UpcomingTaskDto(
                    r.Id,
                    r.Name,
                    r.DeviceId,
                    r.DeviceName,
                    r.DeviceType,
                    r.HouseId,
                    r.HouseName,
                    _calculator.CalculateStatus(nextDueDate, today),
                    nextDueDate,
                    r.LastMaintenanceDate,
                    r.Periodicity.ToString(),
                    r.CustomDays,
                    r.CustomMonths,
                    HousePermissions.CanLogMaintenance(access),
                    HousePermissions.Capabilities(access));
            })
            .OrderBy(t => t.NextDueDate)
            .ThenBy(t => t.MaintenanceTypeName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>R5: owner, RW collaborator, or tenant (unless the owner turned its logging right off).</summary>
    private async Task<HouseAccessInfo> EnsureCanLogAsync(Guid houseId, Guid userId)
    {
        var access = await _memberService.GetAccessInfoAsync(houseId, userId);
        if (access.Role == null)
            throw new UnauthorizedAccessException("Access denied to this device");
        if (!HousePermissions.CanLogMaintenance(access))
            throw new UnauthorizedAccessException("You don't have permission to log maintenance");
        return access;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static MaintenanceTypeDto ToDto(MaintenanceType type) => new(
        type.Id,
        type.Name,
        type.Periodicity,
        type.CustomDays,
        type.CustomMonths,
        type.DeviceId,
        type.CreatedAt
    );

    private static MaintenanceInstanceDto ToDto(MaintenanceInstance instance, string typeName, bool hideCosts = false) => new(
        instance.Id,
        instance.Date,
        hideCosts ? null : instance.Cost,
        hideCosts ? null : instance.Provider,
        instance.Notes,
        instance.MaintenanceTypeId,
        typeName,
        instance.CreatedAt
    );
}
