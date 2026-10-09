using FluentAssertions;
using HouseFlow.Web.Auth;

namespace HouseFlow.UnitTests.Web;

/// <summary>
/// OAuth (#304) front-end helpers, linked from src/HouseFlow.Web/Auth/OAuthReturnUrl.cs: the
/// returnUrl the /oauth pages navigate to (open-redirect guard) and the scopes the consent screen shows.
/// </summary>
public class OAuthReturnUrlTests
{
    private const string Api = "http://localhost:5203";

    private const string AuthorizeRequest =
        "http://localhost:5203/connect/authorize?response_type=code&client_id=a3f0c1&redirect_uri=http%3A%2F%2F127.0.0.1%3A9%2Fcallback"
        + "&scope=houses%3Aread+houses%3Awrite&state=s%2B1&code_challenge=abc&code_challenge_method=S256";

    // ---------- Validate ----------

    [Theory]
    [InlineData(AuthorizeRequest, Api)]
    [InlineData("http://localhost:5203/connect/authorize", Api)]
    [InlineData("http://localhost:5203/connect/authorize?client_id=a", "http://localhost:5203/")]
    [InlineData("http://localhost:5203/connect/authorize?client_id=a", " http://localhost:5203 ")]
    [InlineData("HTTP://LocalHost:5203/connect/authorize?client_id=a", Api)]
    [InlineData("https://api.houseflow.cloud/connect/authorize?client_id=a", "https://api.houseflow.cloud")]
    [InlineData("https://api.houseflow.cloud:443/connect/authorize?client_id=a", "https://api.houseflow.cloud")]
    [InlineData("https://api.houseflow.cloud/connect/authorize?client_id=a", "https://api.houseflow.cloud:443/")]
    [InlineData("http://[::1]:5203/connect/authorize?client_id=a", "http://[::1]:5203")]
    [InlineData("http://localhost:5203/connect/authorize?return=http%3A%2F%2Fevil.example%2F", Api)]
    public void Validate_AcceptsTheApisOwnAuthorizationEndpoint(string returnUrl, string apiBaseUrl)
    {
        OAuthReturnUrl.Validate(returnUrl, apiBaseUrl).Should().Be(returnUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    // Not an absolute http(s) URL.
    [InlineData("/connect/authorize?client_id=a")]
    [InlineData("//localhost:5203/connect/authorize")]
    [InlineData("localhost:5203/connect/authorize")]
    [InlineData("http:/localhost:5203/connect/authorize")]
    [InlineData("http:///localhost:5203/connect/authorize")]
    [InlineData("javascript://localhost:5203/connect/authorize")]
    [InlineData("javascript:alert(1)//http://localhost:5203/connect/authorize")]
    [InlineData("ftp://localhost:5203/connect/authorize")]
    // Another origin.
    [InlineData("https://localhost:5203/connect/authorize")]
    [InlineData("http://localhost:5204/connect/authorize")]
    [InlineData("http://localhost/connect/authorize")]
    [InlineData("http://127.0.0.1:5203/connect/authorize")]
    [InlineData("http://evil.example/connect/authorize")]
    [InlineData("http://localhost.evil.example:5203/connect/authorize")]
    [InlineData("http://evil.example/connect/authorize?next=http://localhost:5203/connect/authorize")]
    [InlineData("http://lоcalhost:5203/connect/authorize")] // Cyrillic « о »
    // Parser tricks: userinfo, backslash, fragment, encoded host.
    [InlineData("http://localhost:5203@evil.example/connect/authorize")]
    [InlineData("http://user@localhost:5203/connect/authorize")]
    [InlineData("http://evil.example\\@localhost:5203/connect/authorize")]
    [InlineData("http://evil.example\\.localhost:5203/connect/authorize")]
    [InlineData("http://evil.example#@localhost:5203/connect/authorize")]
    [InlineData("http://evil.example?@localhost:5203/connect/authorize")]
    [InlineData("http://local%68ost:5203/connect/authorize")]
    [InlineData("http://localhost:5203/connect/authorize#fragment")]
    [InlineData("http://localhost:5203/connect/authorize?client_id=a#")]
    // Not exactly the authorization endpoint.
    [InlineData("http://localhost:5203")]
    [InlineData("http://localhost:5203/")]
    [InlineData("http://localhost:5203?client_id=a")]
    [InlineData("http://localhost:5203/connect/authorize/")]
    [InlineData("http://localhost:5203/connect/Authorize")]
    [InlineData("http://localhost:5203/connect/authorize.json")]
    [InlineData("http://localhost:5203/connect/token")]
    [InlineData("http://localhost:5203/api/v1/users/me")]
    [InlineData("http://localhost:5203/connect/authorize/../../api/v1/users/me")]
    [InlineData("http://localhost:5203/x/../connect/authorize")]
    [InlineData("http://localhost:5203/connect/%61uthorize")]
    [InlineData("http://localhost:5203//connect/authorize")]
    // Whitespace and control characters, which browsers strip or re-encode.
    [InlineData(" http://localhost:5203/connect/authorize")]
    [InlineData("http://localhost:5203/connect/authorize\n")]
    [InlineData("http://localhost:5203/conn\tect/authorize")]
    [InlineData("http://localhost:5203/connect/authorize?scope=a b")]
    [InlineData("http://localhost:5203/connect/authorize?scope=a b")]
    public void Validate_RefusesAnyOtherUrl(string? returnUrl)
    {
        OAuthReturnUrl.Validate(returnUrl, Api).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("/api")]
    [InlineData("ftp://localhost:5203")]
    public void Validate_WithoutAUsableApiBaseUrl_RefusesEverything(string? apiBaseUrl)
    {
        OAuthReturnUrl.Validate("http://localhost:5203/connect/authorize?client_id=a", apiBaseUrl).Should().BeNull();
    }

    // ---------- Parameter / Denied / Host ----------

    [Fact]
    public void Parameter_DecodesTheQueryLikeTheApi()
    {
        OAuthReturnUrl.Parameter(AuthorizeRequest, "client_id").Should().Be("a3f0c1");
        OAuthReturnUrl.Parameter(AuthorizeRequest, "scope").Should().Be("houses:read houses:write");
        OAuthReturnUrl.Parameter(AuthorizeRequest, "redirect_uri").Should().Be("http://127.0.0.1:9/callback");
        OAuthReturnUrl.Parameter(AuthorizeRequest, "state").Should().Be("s+1");
    }

    [Theory]
    [InlineData("http://localhost:5203/connect/authorize")]
    [InlineData("http://localhost:5203/connect/authorize?scope=houses%3Aread")]
    [InlineData("http://localhost:5203/connect/authorize?client_id=")]
    [InlineData("http://localhost:5203/connect/authorize?client_id")]
    [InlineData("http://localhost:5203/connect/authorize?client_id=a&client_id=b")]
    [InlineData("http://localhost:5203/connect/authorize?client_id=a&client%5Fid=b")]
    [InlineData("http://localhost:5203/connect/authorize?CLIENT_ID=a")]
    [InlineData("http://localhost:5203/connect/authorize?xclient_id=a")]
    public void Parameter_AbsentEmptyOrRepeated_IsNull(string returnUrl)
    {
        OAuthReturnUrl.Parameter(returnUrl, "client_id").Should().BeNull();
    }

    [Theory]
    [InlineData("http://localhost:5203/connect/authorize?client_id=a&state=s",
        "http://localhost:5203/connect/authorize?client_id=a&state=s&houseflow_consent=denied")]
    [InlineData("http://localhost:5203/connect/authorize",
        "http://localhost:5203/connect/authorize?houseflow_consent=denied")]
    public void Denied_AppendsTheRefusalMarker(string returnUrl, string expected)
    {
        OAuthReturnUrl.Denied(returnUrl).Should().Be(expected);
    }

    [Theory]
    [InlineData(AuthorizeRequest, "localhost:5203")]
    [InlineData("https://api.houseflow.cloud/connect/authorize?client_id=a", "api.houseflow.cloud")]
    [InlineData("https://api.houseflow.cloud:443/connect/authorize?client_id=a", "api.houseflow.cloud")]
    public void Host_IsTheApisHostAndPort(string returnUrl, string expected)
    {
        OAuthReturnUrl.Host(returnUrl).Should().Be(expected);
    }

    // ---------- OAuthScopes ----------

    private static readonly string[] BothScopes = [OAuthScopes.HousesRead, OAuthScopes.HousesWrite];

    [Theory]
    [InlineData("houses:read houses:write", new[] { "houses:read", "houses:write" })]
    [InlineData("houses:write houses:read", new[] { "houses:read", "houses:write" })]
    [InlineData("offline_access houses:write  houses:read houses:read", new[] { "houses:read", "houses:write" })]
    [InlineData("houses:write", new[] { "houses:write" })]
    [InlineData("offline_access", new string[0])]
    [InlineData("openid profile houses:delete", new string[0])]
    [InlineData("houses:read,houses:write", new string[0])]
    [InlineData("", new string[0])]
    [InlineData(null, new string[0])]
    public void Displayed_IsTheRequestedAllowedAndGrantableScopes(string? requested, string[] expected)
    {
        OAuthScopes.Displayed(requested, BothScopes).Should().Equal(expected);
    }

    [Fact]
    public void Displayed_NeverShowsAScopeTheClientMayNotRequest()
    {
        OAuthScopes.Displayed("houses:read houses:write", [OAuthScopes.HousesRead]).Should().Equal(OAuthScopes.HousesRead);
        OAuthScopes.Displayed("houses:read offline_access", [OAuthScopes.HousesRead, OAuthScopes.OfflineAccess])
            .Should().Equal(OAuthScopes.HousesRead);
        OAuthScopes.Displayed("houses:read", []).Should().BeEmpty();
        OAuthScopes.Displayed("houses:read", null).Should().BeEmpty();
    }

    [Theory]
    [InlineData(new[] { "houses:read", "houses:write" }, "readWrite")]
    [InlineData(new[] { "houses:write", "offline_access", "houses:read" }, "readWrite")]
    [InlineData(new[] { "houses:read" }, "read")]
    [InlineData(new[] { "houses:write" }, "write")]
    [InlineData(new[] { "offline_access" }, null)]
    [InlineData(new string[0], null)]
    public void Summary_NamesWhatAConnectedAppMayDo(string[] scopes, string? expected)
    {
        OAuthScopes.Summary(scopes).Should().Be(expected);
    }

    [Theory]
    [InlineData("houses:read", "housesRead")]
    [InlineData("houses:write", "housesWrite")]
    public void Key_IsTheCatalogueKeyOfTheScope(string scope, string expected)
    {
        OAuthScopes.Key(scope).Should().Be(expected);
    }
}
