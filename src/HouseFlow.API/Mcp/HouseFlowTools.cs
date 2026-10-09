using System.ComponentModel;
using System.Globalization;
using HouseFlow.Application.DTOs;
using HouseFlow.Application.Interfaces;
using HouseFlow.Application.OAuth;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace HouseFlow.API.Mcp;

/// <summary>
/// Tools over the user's household data: read-only ones (<c>houses:read</c>) and the few writing ones
/// (<c>houses:write</c>, never destructive: no deletion through MCP). Their descriptions are read by the LLM: they say
/// what each tool returns and how to chain them (ids come from the previous call). Lists are
/// paginated (<c>limit</c>/<c>offset</c>) to keep answers small.
/// </summary>
[McpServerToolType]
public sealed class HouseFlowTools
{
    public const int DefaultLimit = 25;
    public const int MaxLimit = 50;

    private readonly McpCaller _caller;
    private readonly IHouseService _houses;
    private readonly IDeviceService _devices;
    private readonly IMaintenanceService _maintenance;

    public HouseFlowTools(McpCaller caller, IHouseService houses, IDeviceService devices, IMaintenanceService maintenance)
    {
        _caller = caller;
        _houses = houses;
        _devices = devices;
        _maintenance = maintenance;
    }

    public sealed record Page<T>(IReadOnlyList<T> Items, int Total, int Offset, bool HasMore);

    private static Page<T> Paginate<T>(IEnumerable<T> source, int? limit, int? offset)
    {
        var all = source.ToList();
        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var skip = Math.Max(offset ?? 0, 0);
        return new Page<T>([.. all.Skip(skip).Take(take)], all.Count, skip, skip + take < all.Count);
    }

    /// <summary>
    /// A house or device the user cannot see is reported exactly like one that does not exist, as the
    /// REST API does: no way to probe for other households' ids.
    /// </summary>
    private static async Task<object> Guarded(Func<Task<object>> call)
    {
        try
        {
            return await call();
        }
        catch (Exception e) when (e is UnauthorizedAccessException or KeyNotFoundException)
        {
            throw new McpException("Not found, or you have no access to it.");
        }
    }

    [McpServerTool(Name = "list_houses", ReadOnly = true, Destructive = false), Description(
        "Lists the houses the user can access (owned or shared with them), with their maintenance score. " +
        "Start here: the returned house ids are needed by the other tools.")]
    public async Task<object> ListHouses(
        [Description("Maximum number of houses to return (1-50, default 25).")] int? limit = null,
        [Description("Number of houses to skip, for paging.")] int? offset = null,
        CancellationToken cancellationToken = default)
    {
        var userId = await _caller.BeginAsync("list_houses", OAuthScopes.HousesRead, cancellationToken);
        var result = await _houses.GetUserHousesAsync(userId);
        return new { GlobalScore = result.GlobalScore, Houses = Paginate(result.Houses, limit, offset) };
    }

    [McpServerTool(Name = "get_house", ReadOnly = true, Destructive = false), Description(
        "Details of one house (address, score, devices with their status). Use an id from list_houses.")]
    public async Task<object> GetHouse(
        [Description("Id of the house.")] Guid houseId,
        CancellationToken cancellationToken = default)
    {
        var userId = await _caller.BeginAsync("get_house", OAuthScopes.HousesRead, cancellationToken);
        return await Guarded(async () => await _houses.GetHouseDetailAsync(houseId, userId)
            ?? throw new KeyNotFoundException());
    }

    [McpServerTool(Name = "list_devices", ReadOnly = true, Destructive = false), Description(
        "Lists the devices (boiler, heat pump, roof...) of a house with their maintenance status. " +
        "Use a house id from list_houses.")]
    public async Task<object> ListDevices(
        [Description("Id of the house.")] Guid houseId,
        [Description("Maximum number of devices to return (1-50, default 25).")] int? limit = null,
        [Description("Number of devices to skip, for paging.")] int? offset = null,
        CancellationToken cancellationToken = default)
    {
        var userId = await _caller.BeginAsync("list_devices", OAuthScopes.HousesRead, cancellationToken);
        return await Guarded(async () => Paginate(await _devices.GetHouseDevicesAsync(houseId, userId), limit, offset));
    }

    [McpServerTool(Name = "get_device", ReadOnly = true, Destructive = false), Description(
        "Details of one device: its maintenance types (what must be done and how often) and their next due dates. " +
        "Use a device id from list_devices.")]
    public async Task<object> GetDevice(
        [Description("Id of the device.")] Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        var userId = await _caller.BeginAsync("get_device", OAuthScopes.HousesRead, cancellationToken);
        return await Guarded(async () => await _devices.GetDeviceDetailAsync(deviceId, userId)
            ?? throw new KeyNotFoundException());
    }

    [McpServerTool(Name = "list_interventions", ReadOnly = true, Destructive = false), Description(
        "History of the maintenance interventions already carried out on a device (date, cost, provider, notes), " +
        "most recent first, with the total spent. Use a device id from list_devices.")]
    public async Task<object> ListInterventions(
        [Description("Id of the device.")] Guid deviceId,
        [Description("Maximum number of interventions to return (1-50, default 25).")] int? limit = null,
        [Description("Number of interventions to skip, for paging.")] int? offset = null,
        CancellationToken cancellationToken = default)
    {
        var userId = await _caller.BeginAsync("list_interventions", OAuthScopes.HousesRead, cancellationToken);
        return await Guarded(async () =>
        {
            var history = await _maintenance.GetDeviceMaintenanceHistoryAsync(deviceId, userId);
            return (object)new { history.TotalSpent, Interventions = Paginate(history.Instances, limit, offset) };
        });
    }

    [McpServerTool(Name = "list_upcoming_tasks", ReadOnly = true, Destructive = false), Description(
        "The next maintenance tasks to carry out across all the user's houses, overdue ones first, " +
        "with the number of overdue and pending tasks.")]
    public async Task<object> ListUpcomingTasks(
        [Description("Maximum number of tasks to return (1-50, default 25).")] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var userId = await _caller.BeginAsync("list_upcoming_tasks", OAuthScopes.HousesRead, cancellationToken);
        var result = await _maintenance.GetUpcomingTasksAsync(userId, Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit));
        return result;
    }

    public const int MaxProviderLength = 200;
    public const int MaxNotesLength = 2000;

    [McpServerTool(Name = "log_intervention", ReadOnly = false, Destructive = false, Idempotent = false), Description(
        "Records a maintenance intervention that was carried out (e.g. a boiler service done yesterday). " +
        "Needs the id of the maintenance type from get_device (what was done on which device). " +
        "Creates a new entry each time it is called: check list_interventions first rather than calling it twice. " +
        "Only call it for an intervention the user actually told you about; never guess the date, cost or provider.")]
    public async Task<object> LogIntervention(
        [Description("Id of the maintenance type (from get_device) the intervention belongs to.")] Guid maintenanceTypeId,
        [Description("Day the intervention was carried out, yyyy-MM-dd (Europe/Paris). Not in the future.")] string date,
        [Description("Cost in euros, 0 or more. Omit if unknown.")] decimal? cost = null,
        [Description("Name of the provider (max 200 characters). Omit if unknown.")] string? provider = null,
        [Description("Notes (max 2000 characters). Omit if none.")] string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var userId = await _caller.BeginAsync("log_intervention", OAuthScopes.HousesWrite, cancellationToken);

        // The model may hallucinate arguments: the REST API's validation attributes do not run on a direct
        // service call, so the same limits are enforced here.
        if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            throw new McpException("'date' must be a calendar day formatted yyyy-MM-dd.");
        if (cost is < 0 or > 10_000_000m)
            throw new McpException("'cost' must be between 0 and 10,000,000 euros.");
        if (provider is { Length: > MaxProviderLength })
            throw new McpException($"'provider' is limited to {MaxProviderLength} characters.");
        if (notes is { Length: > MaxNotesLength })
            throw new McpException($"'notes' is limited to {MaxNotesLength} characters.");

        var request = new LogMaintenanceRequestDto(cost, DateTime.SpecifyKind(day, DateTimeKind.Utc), notes, provider);
        try
        {
            return await Guarded(async () => await _maintenance.LogMaintenanceAsync(maintenanceTypeId, request, userId));
        }
        catch (InvalidOperationException e)
        {
            throw new McpException(e.Message); // business rule, e.g. date in the future
        }
    }
}
