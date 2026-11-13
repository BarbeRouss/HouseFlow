using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HouseFlow.IntegrationTests.OAuth;
using Microsoft.EntityFrameworkCore;
using HouseFlow.Infrastructure.Data;
using static HouseFlow.IntegrationTests.TestHelpers;

namespace HouseFlow.IntegrationTests.Mcp;

/// <summary>MCP server (issue #305): Streamable HTTP on /mcp, OAuth-protected, read tools and the write tools (#306).</summary>
[Collection("Integration")]
public class McpServerTests
{
    private readonly IntegrationTestFixture _fixture;
    private readonly OAuthTestClient _oauth;

    public McpServerTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _oauth = new OAuthTestClient(fixture);
    }

    private static readonly string[] WriteTools = ["log_intervention", "update_intervention", "create_device", "update_device"];

    private async Task<(TestUser User, string Token)> ConnectedUserAsync(string scope = "houses:read")
    {
        var user = await _oauth.RegisterUserAsync();
        var clientId = await _oauth.RegisterClientAsync();
        var tokens = await _oauth.ConnectAsync(user, clientId, scope);
        return (user, tokens.AccessToken);
    }

    private Task<HttpResponseMessage> McpAsync(string? token, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _oauth.Http.SendAsync(request);
    }

    private static object ToolCall(string name, object? arguments = null) => new
    {
        jsonrpc = "2.0", id = 1, method = "tools/call",
        @params = new { name, arguments = arguments ?? new { } }
    };

    /// <summary>The JSON-RPC result of a response, whether it came as JSON or as a server-sent event.</summary>
    private static async Task<JsonElement> ResultAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        var data = text.Split('\n').FirstOrDefault(l => l.StartsWith("data:", StringComparison.Ordinal))?[5..] ?? text;
        using var json = JsonDocument.Parse(data);
        return json.RootElement.GetProperty("result").Clone();
    }

    private static JsonElement Content(JsonElement result)
    {
        (result.TryGetProperty("isError", out var isError) && isError.GetBoolean()).Should().BeFalse(result.ToString());
        return JsonDocument.Parse(result.GetProperty("content")[0].GetProperty("text").GetString()!).RootElement.Clone();
    }

    private async Task<(Guid HouseId, Guid DeviceId)> CreateHouseWithDeviceAsync(TestUser user)
    {
        var api = _fixture.CreateApiClient();
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);
        var houseId = await api.CreateHouseAsync("Maison MCP");
        var response = await api.PostAsJsonAsync($"/api/v1/houses/{houseId}/devices",
            new HouseFlow.Contracts.CreateDeviceRequest(maintenanceType: null, name: "Chaudière MCP", type: "Chaudiere",
                brand: null, model: null, installDate: null));
        response.EnsureSuccessStatusCode();
        var device = await response.Content.ReadAsJsonAsync<HouseFlow.Application.DTOs.DeviceDto>();
        return (houseId, device!.Id);
    }

    [Fact]
    public async Task NoToken_Is401_PointingToTheProtectedResourceMetadata()
    {
        var response = await McpAsync(null, ToolCall("list_houses"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var origin = _oauth.Http.BaseAddress!.GetLeftPart(UriPartial.Authority);
        response.Headers.WwwAuthenticate.ToString().Should()
            .Contain($"resource_metadata=\"{origin}/.well-known/oauth-protected-resource\"");
    }

    [Fact]
    public async Task RestApiJwt_IsNotAcceptedByTheMcpServer()
    {
        var user = await _oauth.RegisterUserAsync();

        var response = await McpAsync(user.AccessToken, ToolCall("list_houses"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task OAuthToken_IsNotAcceptedByTheRestApi()
    {
        var (_, token) = await ConnectedUserAsync();

        (await _oauth.SendAsBearer(HttpMethod.Get, "/api/v1/houses", token)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TokenWithoutReadScope_Is403()
    {
        var (_, token) = await ConnectedUserAsync("houses:write");

        var response = await McpAsync(token, ToolCall("list_houses"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ProtectedResourceMetadata_DescribesTheResourceAndItsAuthorizationServer()
    {
        var client = _fixture.CreateApiClient();
        var origin = client.BaseAddress!.GetLeftPart(UriPartial.Authority);

        foreach (var path in new[] { "/.well-known/oauth-protected-resource", "/.well-known/oauth-protected-resource/mcp" })
        {
            var response = await client.GetAsync(path);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = json.RootElement;
            root.GetProperty("resource").GetString().Should().Be(origin + "/mcp");
            root.GetProperty("authorization_servers").EnumerateArray().Select(e => e.GetString()).Should().Equal(origin + "/");
            root.GetProperty("scopes_supported").EnumerateArray().Select(e => e.GetString()).Should().Equal("houses:read", "houses:write");
            root.GetProperty("bearer_methods_supported").EnumerateArray().Select(e => e.GetString()).Should().Equal("header");
        }
    }

    [Fact]
    public async Task ToolsList_ExposesTheTools_WithDescriptionsAndCorrectAnnotations()
    {
        var (_, token) = await ConnectedUserAsync();

        var result = await ResultAsync(await McpAsync(token, new { jsonrpc = "2.0", id = 1, method = "tools/list" }));

        var tools = result.GetProperty("tools").EnumerateArray().ToList();
        tools.Select(t => t.GetProperty("name").GetString()).Should().BeEquivalentTo(
            "list_houses", "get_house", "list_devices", "get_device", "list_interventions", "list_upcoming_tasks",
            "log_intervention", "update_intervention", "create_device", "update_device");
        tools.Should().OnlyContain(t => t.GetProperty("description").GetString()!.Length > 20);
        tools.Should().OnlyContain(t => !t.GetProperty("annotations").GetProperty("destructiveHint").GetBoolean());
        foreach (var tool in tools)
            tool.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean()
                .Should().Be(!WriteTools.Contains(tool.GetProperty("name").GetString()!));
    }

    [Fact]
    public async Task EveryTool_ReturnsTheUsersData()
    {
        var (user, token) = await ConnectedUserAsync();
        var (houseId, deviceId) = await CreateHouseWithDeviceAsync(user);

        var houses = Content(await ResultAsync(await McpAsync(token, ToolCall("list_houses"))));
        houses.GetProperty("houses").GetProperty("items").EnumerateArray()
            .Select(h => h.GetProperty("id").GetGuid()).Should().Equal(houseId);

        var house = Content(await ResultAsync(await McpAsync(token, ToolCall("get_house", new { houseId }))));
        house.GetProperty("name").GetString().Should().Be("Maison MCP");

        var devices = Content(await ResultAsync(await McpAsync(token, ToolCall("list_devices", new { houseId }))));
        devices.GetProperty("items").EnumerateArray().Select(d => d.GetProperty("id").GetGuid()).Should().Equal(deviceId);

        var device = Content(await ResultAsync(await McpAsync(token, ToolCall("get_device", new { deviceId }))));
        device.GetProperty("name").GetString().Should().Be("Chaudière MCP");

        var interventions = Content(await ResultAsync(await McpAsync(token, ToolCall("list_interventions", new { deviceId }))));
        interventions.GetProperty("interventions").GetProperty("total").GetInt32().Should().Be(0);

        var tasks = Content(await ResultAsync(await McpAsync(token, ToolCall("list_upcoming_tasks"))));
        tasks.TryGetProperty("tasks", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Lists_ArePaginated()
    {
        var (user, token) = await ConnectedUserAsync();
        var api = _fixture.CreateApiClient();
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);
        for (var i = 0; i < 3; i++) await api.CreateHouseAsync($"Maison {i}");

        var page = Content(await ResultAsync(await McpAsync(token, ToolCall("list_houses", new { limit = 2 }))))
            .GetProperty("houses");

        page.GetProperty("items").GetArrayLength().Should().Be(2);
        page.GetProperty("total").GetInt32().Should().Be(3);
        page.GetProperty("hasMore").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task AnotherHouseholdsData_IsNeverReachable()
    {
        var (owner, _) = await ConnectedUserAsync();
        var (_, intruderToken) = await ConnectedUserAsync();
        var (houseId, deviceId) = await CreateHouseWithDeviceAsync(owner);

        var houses = Content(await ResultAsync(await McpAsync(intruderToken, ToolCall("list_houses"))));
        houses.GetProperty("houses").GetProperty("items").GetArrayLength().Should().Be(0);

        foreach (var (tool, args) in new (string, object)[]
        {
            ("get_house", new { houseId }), ("list_devices", new { houseId }),
            ("get_device", new { deviceId }), ("list_interventions", new { deviceId })
        })
        {
            var result = await ResultAsync(await McpAsync(intruderToken, ToolCall(tool, args)));
            result.GetProperty("isError").GetBoolean().Should().BeTrue(tool);
            result.GetProperty("content")[0].GetProperty("text").GetString().Should().NotContain("Maison MCP").And.NotContain("Chaudière");
        }
    }

    [Fact]
    public async Task EveryCall_IsAudited_WithoutTokenNorData()
    {
        var (user, token) = await ConnectedUserAsync();
        await CreateHouseWithDeviceAsync(user);

        await ResultAsync(await McpAsync(token, ToolCall("list_houses")));

        await using var db = await _fixture.CreateDbContextAsync();
        var entry = await db.AuditLogs.AsNoTracking()
            .SingleAsync(a => a.UserId == user.Id && a.EntityType == "McpTool");
        entry.EntityId.Should().Be("list_houses");
        entry.Action.Should().Be("McpCall");
        entry.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(5));
        $"{entry.OldValues}{entry.NewValues}{entry.AdditionalData}".Should().NotContain(token).And.NotContain("Maison MCP");
    }

    private async Task<Guid> CreateMaintenanceTypeAsync(TestUser user, Guid deviceId)
    {
        var api = _fixture.CreateApiClient();
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);
        var response = await api.PostAsJsonAsync($"/api/v1/devices/{deviceId}/maintenance-types",
            new HouseFlow.Application.DTOs.CreateMaintenanceTypeRequestDto("Entretien annuel", HouseFlow.Core.Entities.Periodicity.Annual, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadAsJsonAsync<HouseFlow.Application.DTOs.MaintenanceTypeDto>())!.Id;
    }

    private static string Yesterday => DateTime.UtcNow.AddDays(-2).ToString("yyyy-MM-dd");

    private static async Task<string> ErrorTextAsync(HttpResponseMessage response)
    {
        var result = await ResultAsync(response);
        result.GetProperty("isError").GetBoolean().Should().BeTrue(result.ToString());
        return result.GetProperty("content")[0].GetProperty("text").GetString()!;
    }

    [Fact]
    public async Task LogIntervention_WithReadScopeOnly_IsRefused_AndWritesNothing()
    {
        var (user, token) = await ConnectedUserAsync("houses:read");
        var (_, deviceId) = await CreateHouseWithDeviceAsync(user);
        var typeId = await CreateMaintenanceTypeAsync(user, deviceId);

        var error = await ErrorTextAsync(await McpAsync(token, ToolCall("log_intervention", new { maintenanceTypeId = typeId, date = Yesterday })));

        error.Should().Contain("houses:write");
        await using var db = await _fixture.CreateDbContextAsync();
        (await db.MaintenanceInstances.AnyAsync(i => i.MaintenanceTypeId == typeId)).Should().BeFalse();
    }

    [Fact]
    public async Task LogIntervention_WithWriteScope_CreatesTheIntervention_AndIsAudited()
    {
        var (user, token) = await ConnectedUserAsync("houses:read houses:write");
        var (_, deviceId) = await CreateHouseWithDeviceAsync(user);
        var typeId = await CreateMaintenanceTypeAsync(user, deviceId);

        var created = Content(await ResultAsync(await McpAsync(token, ToolCall("log_intervention",
            new { maintenanceTypeId = typeId, date = Yesterday, cost = 120.5, provider = "Dupont SARL", notes = "RAS" }))));

        created.GetProperty("cost").GetDecimal().Should().Be(120.5m);
        var history = Content(await ResultAsync(await McpAsync(token, ToolCall("list_interventions", new { deviceId }))));
        history.GetProperty("interventions").GetProperty("total").GetInt32().Should().Be(1);

        await using var db = await _fixture.CreateDbContextAsync();
        (await db.AuditLogs.AnyAsync(a => a.UserId == user.Id && a.EntityType == "McpTool" && a.EntityId == "log_intervention")).Should().BeTrue();
        (await db.AuditLogs.AnyAsync(a => a.UserId == user.Id && a.EntityType == "MaintenanceInstance")).Should().BeTrue();
    }

    [Fact]
    public async Task LogIntervention_OnAnotherHouseholdsType_IsNotFound()
    {
        var (_, token) = await ConnectedUserAsync("houses:read houses:write");
        var owner = await _oauth.RegisterUserAsync();
        var (_, deviceId) = await CreateHouseWithDeviceAsync(owner);
        var typeId = await CreateMaintenanceTypeAsync(owner, deviceId);

        var error = await ErrorTextAsync(await McpAsync(token, ToolCall("log_intervention", new { maintenanceTypeId = typeId, date = Yesterday })));

        error.Should().Contain("Not found");
        await using var db = await _fixture.CreateDbContextAsync();
        (await db.MaintenanceInstances.AnyAsync(i => i.MaintenanceTypeId == typeId)).Should().BeFalse();
    }

    [Theory]
    [InlineData("hier", 10, "date")]
    [InlineData("2999-01-01", 10, "future")]
    [InlineData("auto", -5, "cost")]
    public async Task LogIntervention_InvalidInput_IsRejected_AndWritesNothing(string date, int cost, string expected)
    {
        var (user, token) = await ConnectedUserAsync("houses:read houses:write");
        var (_, deviceId) = await CreateHouseWithDeviceAsync(user);
        var typeId = await CreateMaintenanceTypeAsync(user, deviceId);
        if (date == "auto") date = Yesterday;

        var error = await ErrorTextAsync(await McpAsync(token, ToolCall("log_intervention", new { maintenanceTypeId = typeId, date, cost })));

        error.Should().ContainEquivalentOf(expected);
        await using var db = await _fixture.CreateDbContextAsync();
        (await db.MaintenanceInstances.AnyAsync(i => i.MaintenanceTypeId == typeId)).Should().BeFalse();
    }

    [Fact]
    public async Task LogIntervention_TooLongProvider_IsRejected()
    {
        var (user, token) = await ConnectedUserAsync("houses:read houses:write");
        var (_, deviceId) = await CreateHouseWithDeviceAsync(user);
        var typeId = await CreateMaintenanceTypeAsync(user, deviceId);

        var error = await ErrorTextAsync(await McpAsync(token, ToolCall("log_intervention",
            new { maintenanceTypeId = typeId, date = Yesterday, provider = new string('x', 201) })));

        error.Should().Contain("provider");
    }

    // ----- update_intervention, create_device, update_device (#306) -----

    private async Task<(Guid DeviceId, Guid InterventionId)> LoggedInterventionAsync(TestUser user, string token)
    {
        var (_, deviceId) = await CreateHouseWithDeviceAsync(user);
        var typeId = await CreateMaintenanceTypeAsync(user, deviceId);
        var created = Content(await ResultAsync(await McpAsync(token, ToolCall("log_intervention",
            new { maintenanceTypeId = typeId, date = Yesterday, cost = 100, provider = "Dupont SARL", notes = "RAS" }))));
        return (deviceId, created.GetProperty("id").GetGuid());
    }

    [Theory]
    [InlineData("update_intervention")]
    [InlineData("create_device")]
    [InlineData("update_device")]
    public async Task WriteTools_WithReadScopeOnly_AreRefused(string tool)
    {
        var (user, token) = await ConnectedUserAsync("houses:read");
        var (houseId, deviceId) = await CreateHouseWithDeviceAsync(user);
        object args = tool switch
        {
            "update_intervention" => new { deviceId, interventionId = Guid.NewGuid(), cost = 1 },
            "create_device" => new { houseId, name = "VMC", type = "VMC" },
            _ => new { deviceId, name = "Renommé" },
        };

        var error = await ErrorTextAsync(await McpAsync(token, ToolCall(tool, args)));

        error.Should().Contain("houses:write");
        await using var db = await _fixture.CreateDbContextAsync();
        (await db.Devices.CountAsync(d => d.HouseId == houseId)).Should().Be(1);
        (await db.Devices.SingleAsync(d => d.Id == deviceId)).Name.Should().Be("Chaudière MCP");
    }

    [Fact]
    public async Task UpdateIntervention_ChangesOnlyThePassedFields_AndIsAudited()
    {
        var (user, token) = await ConnectedUserAsync("houses:read houses:write");
        var (deviceId, interventionId) = await LoggedInterventionAsync(user, token);

        var updated = Content(await ResultAsync(await McpAsync(token, ToolCall("update_intervention",
            new { deviceId, interventionId, cost = 150.25 }))));

        updated.GetProperty("cost").GetDecimal().Should().Be(150.25m);
        updated.GetProperty("provider").GetString().Should().Be("Dupont SARL");
        updated.GetProperty("notes").GetString().Should().Be("RAS");
        await using var db = await _fixture.CreateDbContextAsync();
        var row = await db.MaintenanceInstances.AsNoTracking().SingleAsync(i => i.Id == interventionId);
        row.Cost.Should().Be(150.25m);
        row.Provider.Should().Be("Dupont SARL");
        row.Date.ToString("yyyy-MM-dd").Should().Be(Yesterday);
        (await db.AuditLogs.AnyAsync(a => a.UserId == user.Id && a.EntityType == "McpTool" && a.EntityId == "update_intervention")).Should().BeTrue();
        (await db.AuditLogs.AnyAsync(a => a.UserId == user.Id && a.EntityType == "MaintenanceInstance" && a.Action == "Modified")).Should().BeTrue();
    }

    [Fact]
    public async Task UpdateIntervention_EmptyString_ClearsTheNotes()
    {
        var (user, token) = await ConnectedUserAsync("houses:read houses:write");
        var (deviceId, interventionId) = await LoggedInterventionAsync(user, token);

        await ResultAsync(await McpAsync(token, ToolCall("update_intervention", new { deviceId, interventionId, notes = "" })));

        await using var db = await _fixture.CreateDbContextAsync();
        var row = await db.MaintenanceInstances.AsNoTracking().SingleAsync(i => i.Id == interventionId);
        row.Notes.Should().BeNull();
        row.Cost.Should().Be(100m);
    }

    [Fact]
    public async Task UpdateIntervention_OnAnotherHouseholdsIntervention_IsNotFound_AndChangesNothing()
    {
        var owner = await _oauth.RegisterUserAsync();
        var ownerToken = (await _oauth.ConnectAsync(owner, await _oauth.RegisterClientAsync(), "houses:read houses:write")).AccessToken;
        var (deviceId, interventionId) = await LoggedInterventionAsync(owner, ownerToken);
        var (_, token) = await ConnectedUserAsync("houses:read houses:write");

        var error = await ErrorTextAsync(await McpAsync(token, ToolCall("update_intervention", new { deviceId, interventionId, cost = 1 })));

        error.Should().Contain("Not found");
        await using var db = await _fixture.CreateDbContextAsync();
        (await db.MaintenanceInstances.AsNoTracking().SingleAsync(i => i.Id == interventionId)).Cost.Should().Be(100m);
    }

    [Fact]
    public async Task UpdateIntervention_UnknownIntervention_IsNotFound()
    {
        var (user, token) = await ConnectedUserAsync("houses:read houses:write");
        var (deviceId, _) = await LoggedInterventionAsync(user, token);

        var error = await ErrorTextAsync(await McpAsync(token, ToolCall("update_intervention", new { deviceId, interventionId = Guid.NewGuid(), cost = 1 })));

        error.Should().Contain("Not found");
    }

    [Theory]
    [InlineData("hier", 10, "date")]
    [InlineData("2999-01-01", 10, "future")]
    [InlineData("auto", -5, "cost")]
    [InlineData("auto", 2_000_000, "cost")]
    [InlineData("auto", null, "at least one")]
    public async Task UpdateIntervention_InvalidInput_IsRejected_AndChangesNothing(string date, int? cost, string expected)
    {
        var (user, token) = await ConnectedUserAsync("houses:read houses:write");
        var (deviceId, interventionId) = await LoggedInterventionAsync(user, token);
        object args = date == "auto" && cost is null
            ? new { deviceId, interventionId }
            : new { deviceId, interventionId, date = date == "auto" ? null : date, cost };

        var error = await ErrorTextAsync(await McpAsync(token, ToolCall("update_intervention", args)));

        error.Should().ContainEquivalentOf(expected);
        await using var db = await _fixture.CreateDbContextAsync();
        var row = await db.MaintenanceInstances.AsNoTracking().SingleAsync(i => i.Id == interventionId);
        row.Cost.Should().Be(100m);
        row.Date.ToString("yyyy-MM-dd").Should().Be(Yesterday);
    }

    [Fact]
    public async Task CreateDevice_WithWriteScope_CreatesTheDevice_AndIsAudited()
    {
        var (user, token) = await ConnectedUserAsync("houses:read houses:write");
        var (houseId, _) = await CreateHouseWithDeviceAsync(user);

        var created = Content(await ResultAsync(await McpAsync(token, ToolCall("create_device",
            new { houseId, name = " Pompe à chaleur ", type = "Pompe à Chaleur", brand = "Atlantic", installDate = "2024-03-15" }))));

        created.GetProperty("name").GetString().Should().Be("Pompe à chaleur");
        var devices = Content(await ResultAsync(await McpAsync(token, ToolCall("list_devices", new { houseId }))));
        devices.GetProperty("total").GetInt32().Should().Be(2);
        await using var db = await _fixture.CreateDbContextAsync();
        var row = await db.Devices.AsNoTracking().SingleAsync(d => d.Id == created.GetProperty("id").GetGuid());
        row.Brand.Should().Be("Atlantic");
        row.InstallDate!.Value.ToString("yyyy-MM-dd").Should().Be("2024-03-15");
        (await db.AuditLogs.AnyAsync(a => a.UserId == user.Id && a.EntityType == "McpTool" && a.EntityId == "create_device")).Should().BeTrue();
        (await db.AuditLogs.AnyAsync(a => a.UserId == user.Id && a.EntityType == "Device")).Should().BeTrue();
    }

    [Fact]
    public async Task CreateDevice_OnAnotherHouseholdsHouse_IsNotFound_AndCreatesNothing()
    {
        var (_, token) = await ConnectedUserAsync("houses:read houses:write");
        var owner = await _oauth.RegisterUserAsync();
        var (houseId, _) = await CreateHouseWithDeviceAsync(owner);

        var error = await ErrorTextAsync(await McpAsync(token, ToolCall("create_device", new { houseId, name = "Intrus", type = "VMC" })));

        error.Should().Contain("Not found");
        await using var db = await _fixture.CreateDbContextAsync();
        (await db.Devices.CountAsync(d => d.HouseId == houseId)).Should().Be(1);
    }

    [Theory]
    [InlineData("", "VMC", "name")]
    [InlineData("   ", "VMC", "name")]
    [InlineData("VMC", "", "type")]
    [InlineData("LONG", "VMC", "name")]
    public async Task CreateDevice_InvalidInput_IsRejected_AndCreatesNothing(string name, string type, string expected)
    {
        var (user, token) = await ConnectedUserAsync("houses:read houses:write");
        var (houseId, _) = await CreateHouseWithDeviceAsync(user);
        if (name == "LONG") name = new string('x', 201);

        var error = await ErrorTextAsync(await McpAsync(token, ToolCall("create_device", new { houseId, name, type })));

        error.Should().Contain(expected);
        await using var db = await _fixture.CreateDbContextAsync();
        (await db.Devices.CountAsync(d => d.HouseId == houseId)).Should().Be(1);
    }

    [Fact]
    public async Task CreateDevice_BadInstallDate_IsRejected()
    {
        var (user, token) = await ConnectedUserAsync("houses:read houses:write");
        var (houseId, _) = await CreateHouseWithDeviceAsync(user);

        var error = await ErrorTextAsync(await McpAsync(token, ToolCall("create_device", new { houseId, name = "VMC", type = "VMC", installDate = "mars 2024" })));

        error.Should().Contain("installDate");
    }

    [Fact]
    public async Task UpdateDevice_ChangesOnlyThePassedFields_AndIsAudited()
    {
        var (user, token) = await ConnectedUserAsync("houses:read houses:write");
        var (houseId, _) = await CreateHouseWithDeviceAsync(user);
        var created = Content(await ResultAsync(await McpAsync(token, ToolCall("create_device",
            new { houseId, name = "VMC", type = "VMC", brand = "Aldes", model = "X1", installDate = "2023-01-02" }))));
        var deviceId = created.GetProperty("id").GetGuid();

        var updated = Content(await ResultAsync(await McpAsync(token, ToolCall("update_device", new { deviceId, name = "VMC double flux" }))));

        updated.GetProperty("name").GetString().Should().Be("VMC double flux");
        await using var db = await _fixture.CreateDbContextAsync();
        var row = await db.Devices.AsNoTracking().SingleAsync(d => d.Id == deviceId);
        row.Name.Should().Be("VMC double flux");
        row.Type.Should().Be("VMC");
        row.Brand.Should().Be("Aldes");
        row.Model.Should().Be("X1");
        row.InstallDate!.Value.ToString("yyyy-MM-dd").Should().Be("2023-01-02");
        (await db.AuditLogs.AnyAsync(a => a.UserId == user.Id && a.EntityType == "McpTool" && a.EntityId == "update_device")).Should().BeTrue();
    }

    [Fact]
    public async Task UpdateDevice_EmptyString_ClearsTheBrand()
    {
        var (user, token) = await ConnectedUserAsync("houses:read houses:write");
        var (houseId, _) = await CreateHouseWithDeviceAsync(user);
        var created = Content(await ResultAsync(await McpAsync(token, ToolCall("create_device", new { houseId, name = "VMC", type = "VMC", brand = "Aldes", model = "X1" }))));
        var deviceId = created.GetProperty("id").GetGuid();

        await ResultAsync(await McpAsync(token, ToolCall("update_device", new { deviceId, brand = "" })));

        await using var db = await _fixture.CreateDbContextAsync();
        var row = await db.Devices.AsNoTracking().SingleAsync(d => d.Id == deviceId);
        row.Brand.Should().BeNull();
        row.Model.Should().Be("X1");
    }

    [Fact]
    public async Task UpdateDevice_OnAnotherHouseholdsDevice_IsNotFound_AndChangesNothing()
    {
        var (_, token) = await ConnectedUserAsync("houses:read houses:write");
        var owner = await _oauth.RegisterUserAsync();
        var (_, deviceId) = await CreateHouseWithDeviceAsync(owner);

        var error = await ErrorTextAsync(await McpAsync(token, ToolCall("update_device", new { deviceId, name = "Piraté" })));

        error.Should().Contain("Not found");
        await using var db = await _fixture.CreateDbContextAsync();
        (await db.Devices.AsNoTracking().SingleAsync(d => d.Id == deviceId)).Name.Should().Be("Chaudière MCP");
    }

    [Theory]
    [InlineData("", "name")]
    [InlineData("NOTHING", "at least one")]
    public async Task UpdateDevice_InvalidInput_IsRejected_AndChangesNothing(string name, string expected)
    {
        var (user, token) = await ConnectedUserAsync("houses:read houses:write");
        var (_, deviceId) = await CreateHouseWithDeviceAsync(user);
        object args = name == "NOTHING" ? new { deviceId } : new { deviceId, name };

        var error = await ErrorTextAsync(await McpAsync(token, ToolCall("update_device", args)));

        error.Should().Contain(expected);
        await using var db = await _fixture.CreateDbContextAsync();
        (await db.Devices.AsNoTracking().SingleAsync(d => d.Id == deviceId)).Name.Should().Be("Chaudière MCP");
    }
}
