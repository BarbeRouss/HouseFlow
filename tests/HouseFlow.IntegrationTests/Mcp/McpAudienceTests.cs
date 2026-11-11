using System.Security.Claims;
using FluentAssertions;
using HouseFlow.API.Mcp;
using HouseFlow.API.OAuth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;

namespace HouseFlow.IntegrationTests.Mcp;

/// <summary>
/// The audience check of the MCP policy (RFC 8707): a valid token issued for another resource must
/// not be replayable on /mcp. Tokens cannot be issued for another resource through the running
/// server (it only issues for its own), so the decision is exercised directly.
/// </summary>
public class McpAudienceTests
{
    private const string Mcp = "https://api.houseflow.test/mcp";

    private static HttpContext HttpContextFor(params string[] configuredResources)
    {
        var services = new ServiceCollection()
            .AddSingleton<IOptions<OAuthOptions>>(Options.Create(new OAuthOptions { Resources = configuredResources }))
            .BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("api.houseflow.test");
        return http;
    }

    private static ClaimsPrincipal Token(string scope, params string[] audiences)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity("test"));
        principal.SetScopes(scope);
        principal.SetAudiences(audiences);
        return principal;
    }

    [Fact]
    public void TokenForThisResource_WithReadScope_IsAccepted() =>
        McpCaller.IsTokenForThisServer(HttpContextFor(), Token("houses:read", Mcp)).Should().BeTrue();

    [Fact]
    public void TokenForThisResource_MatchesTheConfiguredResource() =>
        McpCaller.IsTokenForThisServer(HttpContextFor(Mcp), Token("houses:read", Mcp)).Should().BeTrue();

    [Theory]
    [InlineData("https://other.example/mcp")]
    [InlineData("https://api.houseflow.test/api/v1")]
    [InlineData("https://api.houseflow.test/mcp/other")]
    public void TokenForAnotherResource_IsRefused(string audience) =>
        McpCaller.IsTokenForThisServer(HttpContextFor(), Token("houses:read", audience)).Should().BeFalse();

    [Fact]
    public void TokenWithoutAudience_IsRefused() =>
        McpCaller.IsTokenForThisServer(HttpContextFor(), Token("houses:read")).Should().BeFalse();

    [Fact]
    public void TokenForAnotherResource_IsRefused_EvenWhenOneAudienceMatchesNothingConfigured() =>
        McpCaller.IsTokenForThisServer(HttpContextFor("https://configured.example/mcp"), Token("houses:read", Mcp)).Should().BeFalse();

    [Fact]
    public void TokenForThisResource_WithoutReadScope_IsRefused() =>
        McpCaller.IsTokenForThisServer(HttpContextFor(), Token("houses:write", Mcp)).Should().BeFalse();
}
