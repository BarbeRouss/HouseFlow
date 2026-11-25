using FluentAssertions;
using HouseFlow.Web.Features.Settings;

namespace HouseFlow.UnitTests.Web;

/// <summary>
/// #386: the MCP server address shown in P11 « Connecter Claude », derived from the API base URL
/// (linked from src/HouseFlow.Web/Features/Settings/McpServerUrl.cs).
/// </summary>
public class McpServerUrlTests
{
    [Theory]
    [InlineData("https://api.houseflow.cloud", "https://api.houseflow.cloud/mcp")]
    [InlineData("https://api.houseflow.cloud/", "https://api.houseflow.cloud/mcp")]
    [InlineData(" https://api.houseflow.cloud/ ", "https://api.houseflow.cloud/mcp")]
    [InlineData("https://API.HouseFlow.cloud", "https://api.houseflow.cloud/mcp")]
    [InlineData("https://api.houseflow.cloud:443", "https://api.houseflow.cloud/mcp")]
    [InlineData("https://api-386.pr.houseflow.cloud", "https://api-386.pr.houseflow.cloud/mcp")]
    [InlineData("http://localhost:5203", "http://localhost:5203/mcp")]
    [InlineData("http://localhost:5203/", "http://localhost:5203/mcp")]
    [InlineData("https://houseflow.example/base/", "https://houseflow.example/base/mcp")]
    public void From_AppendsMcpPathToTheApiOrigin(string apiBaseUrl, string expected) =>
        McpServerUrl.From(apiBaseUrl).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/api")]
    [InlineData("api.houseflow.cloud")]
    [InlineData("ftp://api.houseflow.cloud")]
    public void From_IsEmptyWhenTheApiBaseUrlIsNotAbsoluteHttp(string? apiBaseUrl) =>
        McpServerUrl.From(apiBaseUrl).Should().BeEmpty();
}
