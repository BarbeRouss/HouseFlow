using System.ComponentModel;
using System.Globalization;
using HouseFlow.Application.DTOs;
using HouseFlow.Application.Interfaces;
using HouseFlow.Application.OAuth;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace HouseFlow.API.Mcp;

/// <summary>
/// Tools over the user's household data: read-only ones (<c>houses:read</c>) and the writing ones
/// (<c>houses:write</c>: log/correct an intervention, add/change a device; never destructive: no deletion through MCP). Their descriptions are read by the LLM: they say
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
    public const int MaxNameLength = 200;
    public const decimal MaxCost = 1_000_000m;

    // The model may hallucinate arguments, and the REST API's validation attributes do not run on a direct service
    // call: the same limits are enforced here, for every write tool.
    private static DateTime ParseDay(string value, string field)
    {
        if (!DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            throw new McpException($"'{field}' must be a calendar day formatted yyyy-MM-dd.");
        return DateTime.SpecifyKind(day, DateTimeKind.Utc);
    }

    private static void CheckCost(decimal? cost)
    {
        if (cost is < 0 or > MaxCost)
            throw new McpException($"'cost' must be between 0 and {MaxCost:N0} euros.");
    }

    private static void CheckLength(string? value, string field, int max)
    {
        if (value is not null && value.Length > max)
            throw new McpException($"'{field}' is limited to {max} characters.");
    }

    private static void CheckRequired(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new McpException($"'{field}' is required.");
    }

    /// <summary>A business rule of the service (e.g. date in the future) is reported to the model as is.</summary>
    private static async Task<object> Guarded(Func<Task<object>> call, bool businessRules)
    {
        try
        {
            return await Guarded(call);
        }
        catch (InvalidOperationException e) when (businessRules)
        {
            throw new McpException(e.Message);
        }
    }

    [McpServerTool(Name = "log_intervention", ReadOnly = false, Destructive = false, Idempotent = false), Description(
        "Records a maintenance intervention that was carried out (e.g. a boiler service done yesterday). " +
        "Needs the id of the maintenance type from get_device (what was done on which device). " +
        "Creates a new entry each time it is called: check list_interventions first rather than calling it twice. " +
        "Only call it for an intervention the user actually told you about; never guess the date, cost or provider.")]
    public async Task<object> LogIntervention(
        [Description("Id of the maintenance type (from get_device) the intervention belongs to.")] Guid maintenanceTypeId,
        [Description("Day the intervention was carried out, yyyy-MM-dd (Europe/Paris). Not in the future.")] string date,
        [Description("Cost in euros, 0 to 1,000,000. Omit if unknown.")] decimal? cost = null,
        [Description("Name of the provider (max 200 characters). Omit if unknown.")] string? provider = null,
        [Description("Notes (max 2000 characters). Omit if none.")] string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var userId = await _caller.BeginAsync("log_intervention", OAuthScopes.HousesWrite, cancellationToken);

        var day = ParseDay(date, "date");
        CheckCost(cost);
        CheckLength(provider, "provider", MaxProviderLength);
        CheckLength(notes, "notes", MaxNotesLength);

        var request = new LogMaintenanceRequestDto(cost, day, notes, provider);
        return await Guarded(async () => await _maintenance.LogMaintenanceAsync(maintenanceTypeId, request, userId), businessRules: true);
    }

    [McpServerTool(Name = "update_intervention", ReadOnly = false, Destructive = false, Idempotent = true), Description(
        "Corrects an intervention already recorded (wrong date, cost, provider or notes). " +
        "Needs the device id and the intervention id, both from list_interventions. " +
        "Only the fields you pass are changed; the others keep their value. To empty the provider or the notes, pass an " +
        "empty string; a cost cannot be removed, only changed. " +
        "Only call it for a correction the user actually asked for; never guess a value.")]
    public async Task<object> UpdateIntervention(
        [Description("Id of the device the intervention belongs to (from list_devices).")] Guid deviceId,
        [Description("Id of the intervention (from list_interventions).")] Guid interventionId,
        [Description("New day, yyyy-MM-dd (Europe/Paris). Not in the future. Omit to keep it.")] string? date = null,
        [Description("New cost in euros, 0 to 1,000,000. Omit to keep it.")] decimal? cost = null,
        [Description("New provider name (max 200 characters); empty string to clear. Omit to keep it.")] string? provider = null,
        [Description("New notes (max 2000 characters); empty string to clear. Omit to keep them.")] string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var userId = await _caller.BeginAsync("update_intervention", OAuthScopes.HousesWrite, cancellationToken);

        DateTime? day = date is null ? null : ParseDay(date, "date");
        CheckCost(cost);
        CheckLength(provider, "provider", MaxProviderLength);
        CheckLength(notes, "notes", MaxNotesLength);
        if (day is null && cost is null && provider is null && notes is null)
            throw new McpException("Pass at least one of 'date', 'cost', 'provider' or 'notes' to change.");

        return await Guarded(async () =>
        {
            // The service replaces every optional field: start from the current values so that an omitted
            // argument means "unchanged", whatever the model forgot to repeat. The history is access-checked.
            var history = await _maintenance.GetDeviceMaintenanceHistoryAsync(deviceId, userId);
            var current = history.Instances.FirstOrDefault(i => i.Id == interventionId)
                ?? throw new KeyNotFoundException();
            var request = new UpdateMaintenanceInstanceRequestDto(
                day,
                cost ?? current.Cost,
                provider ?? current.Provider,
                notes ?? current.Notes);
            return await _maintenance.UpdateMaintenanceInstanceAsync(interventionId, request, userId)
                ?? throw new KeyNotFoundException();
        }, businessRules: true);
    }

    [McpServerTool(Name = "create_device", ReadOnly = false, Destructive = false, Idempotent = false), Description(
        "Adds a device (boiler, heat pump, smoke detector...) to a house. Use a house id from list_houses. " +
        "Creates a new device each time it is called: check list_devices first rather than calling it twice. " +
        "'type' is the kind of device, e.g. \"Chaudière Gaz\", \"Pompe à Chaleur\", \"VMC\" or \"Autre\". " +
        "The device starts without any maintenance type: those are added in the HouseFlow app. " +
        "Only call it for a device the user actually told you about.")]
    public async Task<object> CreateDevice(
        [Description("Id of the house (from list_houses).")] Guid houseId,
        [Description("Name of the device (1-200 characters), e.g. \"Chaudière cuisine\".")] string name,
        [Description("Kind of device (e.g. \"Chaudière Gaz\", \"VMC\", \"Autre\"), max 200 characters.")] string type,
        [Description("Brand (max 200 characters). Omit if unknown.")] string? brand = null,
        [Description("Model (max 200 characters). Omit if unknown.")] string? model = null,
        [Description("Installation day, yyyy-MM-dd. Omit if unknown.")] string? installDate = null,
        CancellationToken cancellationToken = default)
    {
        var userId = await _caller.BeginAsync("create_device", OAuthScopes.HousesWrite, cancellationToken);

        CheckRequired(name, "name");
        CheckRequired(type, "type");
        CheckLength(name, "name", MaxNameLength);
        CheckLength(type, "type", MaxNameLength);
        CheckLength(brand, "brand", MaxNameLength);
        CheckLength(model, "model", MaxNameLength);
        DateTime? installed = installDate is null ? null : ParseDay(installDate, "installDate");

        var request = new CreateDeviceRequestDto(brand, installed, null, model, name.Trim(), type.Trim());
        return await Guarded(async () => await _devices.CreateDeviceAsync(houseId, request, userId));
    }

    [McpServerTool(Name = "update_device", ReadOnly = false, Destructive = false, Idempotent = true), Description(
        "Changes the name, type, brand, model or installation day of a device. Use a device id from list_devices. " +
        "Only the fields you pass are changed; the others keep their value. To empty the brand or the model, pass an " +
        "empty string. Only call it for a change the user actually asked for.")]
    public async Task<object> UpdateDevice(
        [Description("Id of the device (from list_devices).")] Guid deviceId,
        [Description("New name (1-200 characters). Omit to keep it.")] string? name = null,
        [Description("New kind of device (max 200 characters). Omit to keep it.")] string? type = null,
        [Description("New brand (max 200 characters); empty string to clear. Omit to keep it.")] string? brand = null,
        [Description("New model (max 200 characters); empty string to clear. Omit to keep it.")] string? model = null,
        [Description("New installation day, yyyy-MM-dd. Omit to keep it.")] string? installDate = null,
        CancellationToken cancellationToken = default)
    {
        var userId = await _caller.BeginAsync("update_device", OAuthScopes.HousesWrite, cancellationToken);

        if (name is not null) CheckRequired(name, "name");
        if (type is not null) CheckRequired(type, "type");
        CheckLength(name, "name", MaxNameLength);
        CheckLength(type, "type", MaxNameLength);
        CheckLength(brand, "brand", MaxNameLength);
        CheckLength(model, "model", MaxNameLength);
        DateTime? installed = installDate is null ? null : ParseDay(installDate, "installDate");
        if (name is null && type is null && brand is null && model is null && installed is null)
            throw new McpException("Pass at least one of 'name', 'type', 'brand', 'model' or 'installDate' to change.");

        return await Guarded(async () =>
        {
            // The service replaces brand, model and install day: start from the current values (access-checked).
            var current = await _devices.GetDeviceDetailAsync(deviceId, userId) ?? throw new KeyNotFoundException();
            var request = new UpdateDeviceRequestDto(
                brand ?? current.Brand,
                installed ?? current.InstallDate,
                model ?? current.Model,
                name?.Trim(),
                type?.Trim());
            return await _devices.UpdateDeviceAsync(deviceId, request, userId) ?? throw new KeyNotFoundException();
        });
    }
}
