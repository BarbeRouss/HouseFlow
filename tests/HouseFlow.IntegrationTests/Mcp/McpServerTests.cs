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

/// <summary>MCP server (issue #305): Streamable HTTP on /mcp, OAuth-protected, read-only tools.</summary>
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
    public async Task ToolsList_ExposesTheSixReadOnlyTools_WithDescriptions()
    {
        var (_, token) = await ConnectedUserAsync();

        var result = await ResultAsync(await McpAsync(token, new { jsonrpc = "2.0", id = 1, method = "tools/list" }));

        var tools = result.GetProperty("tools").EnumerateArray().ToList();
        tools.Select(t => t.GetProperty("name").GetString()).Should().BeEquivalentTo(
            "list_houses", "get_house", "list_devices", "get_device", "list_interventions", "list_upcoming_tasks");
        tools.Should().OnlyContain(t => t.GetProperty("description").GetString()!.Length > 20);
        tools.Should().OnlyContain(t => t.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean());
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
}
