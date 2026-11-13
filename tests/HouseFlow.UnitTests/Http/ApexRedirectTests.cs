using FluentAssertions;
using HouseFlow.API.Configuration;
using HouseFlow.API.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace HouseFlow.UnitTests.Http;

/// <summary>Apex domain → www redirection (issue #217).</summary>
public class ApexRedirectTests
{
    private static ApexRedirectOptions Configured(string? host = "houseflow.cloud", string? target = "https://www.houseflow.cloud")
    {
        var values = new Dictionary<string, string?>();
        if (host != null) values["ApexRedirect:Host"] = host;
        if (target != null) values["ApexRedirect:Target"] = target;
        return ApexRedirectOptions.From(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
    }

    private static async Task<(DefaultHttpContext Context, bool NextCalled)> SendAsync(
        ApexRedirectOptions options, string host, string method = "GET", string path = "/", string query = "", string pathBase = "")
    {
        var nextCalled = false;
        var middleware = new ApexRedirectMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, options);
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Host = new HostString(host);
        context.Request.PathBase = pathBase;
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);
        await middleware.InvokeAsync(context);
        return (context, nextCalled);
    }

    [Theory]
    [InlineData("GET", "/", "", "https://www.houseflow.cloud/")]
    [InlineData("GET", "/login", "?x=1", "https://www.houseflow.cloud/login?x=1")]
    [InlineData("POST", "/login", "?x=1&y=a%20b", "https://www.houseflow.cloud/login?x=1&y=a%20b")]
    [InlineData("GET", "/a b/é", "", "https://www.houseflow.cloud/a%20b/%C3%A9")]
    public async Task ApexHost_Gets308_ToTheSamePathAndQueryOnWww(string method, string path, string query, string expected)
    {
        var (context, nextCalled) = await SendAsync(Configured(), "houseflow.cloud", method, path, query);

        context.Response.StatusCode.Should().Be(StatusCodes.Status308PermanentRedirect);
        context.Response.Headers.Location.ToString().Should().Be(expected);
        nextCalled.Should().BeFalse();
    }

    [Theory]
    [InlineData("HouseFlow.Cloud")]
    [InlineData("houseflow.cloud:443")]
    public async Task ApexHost_IsMatchedWithoutCaseOrPort(string host)
    {
        var (context, _) = await SendAsync(Configured(), host, path: "/x");

        context.Response.StatusCode.Should().Be(StatusCodes.Status308PermanentRedirect);
        context.Response.Headers.Location.ToString().Should().Be("https://www.houseflow.cloud/x");
    }

    [Theory]
    [InlineData("api.houseflow.cloud")]
    [InlineData("www.houseflow.cloud")]
    [InlineData("evilhouseflow.cloud")]
    [InlineData("houseflow.cloud.evil.example")]
    public async Task OtherHosts_PassThrough(string host)
    {
        var (context, nextCalled) = await SendAsync(Configured(), host);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.Response.Headers.Location.Should().BeEmpty();
    }

    [Fact]
    public void WithoutConfiguration_TheRedirectIsDisabled()
    {
        var options = ApexRedirectOptions.From(new ConfigurationBuilder().Build());

        options.Enabled.Should().BeFalse();
    }

    [Fact]
    public void Target_IsNormalisedToItsOrigin()
    {
        Configured(target: "https://WWW.houseflow.cloud/").Target.Should().Be("https://www.houseflow.cloud");
    }

    [Theory]
    [InlineData("houseflow.cloud", null)]
    [InlineData("houseflow.cloud", "")]
    [InlineData("houseflow.cloud", "http://www.houseflow.cloud")]
    [InlineData("houseflow.cloud", "www.houseflow.cloud")]
    [InlineData("houseflow.cloud", "https://www.houseflow.cloud/app")]
    [InlineData("houseflow.cloud", "https://www.houseflow.cloud/?x=1")]
    [InlineData("houseflow.cloud", "https://houseflow.cloud")]
    [InlineData("https://houseflow.cloud", "https://www.houseflow.cloud")]
    [InlineData("houseflow.cloud:443", "https://www.houseflow.cloud")]
    public void InvalidConfiguration_FailsFast(string host, string? target)
    {
        var act = () => Configured(host, target);

        act.Should().Throw<InvalidOperationException>().WithMessage("ApexRedirect:*");
    }
}
