using FluentAssertions;
using HouseFlow.Application.OAuth;

namespace HouseFlow.UnitTests.OAuth;

public class ClientRegistrationValidatorTests
{
    private static ClientRegistrationRequest Valid(
        string? clientName = "Claude",
        List<string?>? redirectUris = null,
        string? authMethod = null,
        List<string?>? grantTypes = null,
        List<string?>? responseTypes = null,
        string? scope = null,
        string? clientUri = null) => new()
    {
        ClientName = clientName,
        RedirectUris = redirectUris ?? ["https://claude.ai/api/mcp/auth_callback"],
        TokenEndpointAuthMethod = authMethod,
        GrantTypes = grantTypes,
        ResponseTypes = responseTypes,
        Scope = scope,
        ClientUri = clientUri
    };

    /// <summary>HouseFlow deployed: its API and front end, plus a local development instance.</summary>
    private static readonly Uri[] HouseFlowOrigins =
    [
        new("https://api.houseflow.app"),
        new("https://www.houseflow.app"),
        new("http://localhost:5203"),
        new("http://localhost:3000")
    ];

    private static ValidatedClientRegistration Accept(ClientRegistrationRequest request)
    {
        ClientRegistrationValidator.TryValidate(request, HouseFlowOrigins, out var registration, out var error)
            .Should().BeTrue(error?.ErrorDescription);
        return registration!;
    }

    private static ClientRegistrationError Refuse(ClientRegistrationRequest? request)
    {
        ClientRegistrationValidator.TryValidate(request, HouseFlowOrigins, out _, out var error).Should().BeFalse();
        return error!;
    }

    [Fact]
    public void MinimalRegistration_GetsTheDefaults()
    {
        var registration = Accept(Valid());

        registration.ClientName.Should().Be("Claude");
        registration.GrantTypes.Should().Equal("authorization_code", "refresh_token");
        registration.AllowsRefreshTokens.Should().BeTrue();
        registration.ResponseTypes.Should().Equal("code");
        registration.Scopes.Should().Equal(OAuthScopes.HousesRead, OAuthScopes.HousesWrite);
        registration.ClientUri.Should().BeNull();
    }

    [Fact]
    public void ExplicitMetadata_IsKept()
    {
        var registration = Accept(Valid(
            clientName: "  Claude Code  ",
            redirectUris: ["http://127.0.0.1:9/cb", "http://localhost:8080/cb", "http://127.0.0.1:9/cb"],
            authMethod: "none",
            grantTypes: ["authorization_code"],
            responseTypes: ["code"],
            scope: "houses:read offline_access",
            clientUri: "https://claude.ai"));

        registration.ClientName.Should().Be("Claude Code");
        registration.RedirectUris.Should().Equal("http://127.0.0.1:9/cb", "http://localhost:8080/cb");
        registration.GrantTypes.Should().Equal("authorization_code");
        registration.AllowsRefreshTokens.Should().BeFalse();
        registration.Scopes.Should().ContainSingle()
            .Which.Should().Be(OAuthScopes.HousesRead, "offline_access is a protocol scope, not a permission");
        registration.ClientUri.Should().Be("https://claude.ai");
    }

    [Fact]
    public void NoBody_IsInvalidClientMetadata() =>
        Refuse(null).Error.Should().Be(ClientRegistrationError.InvalidClientMetadata);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingClientName_IsInvalidClientMetadata(string? name) =>
        Refuse(Valid(clientName: name)).Error.Should().Be(ClientRegistrationError.InvalidClientMetadata);

    [Fact]
    public void TooLongClientName_IsInvalidClientMetadata() =>
        Refuse(Valid(clientName: new string('a', ClientRegistrationValidator.MaxClientNameLength + 1)))
            .Error.Should().Be(ClientRegistrationError.InvalidClientMetadata);

    [Theory]
    [InlineData(0x202E)] // right-to-left override
    [InlineData(0x2066)] // left-to-right isolate
    [InlineData(0x200F)] // right-to-left mark
    [InlineData(0x0007)] // control character
    public void SpoofingCharacterInClientName_IsInvalidClientMetadata(int codePoint) =>
        Refuse(Valid(clientName: "Claude" + (char)codePoint + "evil"))
            .Error.Should().Be(ClientRegistrationError.InvalidClientMetadata);

    [Fact]
    public void MissingRedirectUris_IsInvalidRedirectUri()
    {
        Refuse(new ClientRegistrationRequest { ClientName = "Claude" }).Error.Should().Be(ClientRegistrationError.InvalidRedirectUri);
        Refuse(Valid(redirectUris: [])).Error.Should().Be(ClientRegistrationError.InvalidRedirectUri);
    }

    [Fact]
    public void TooManyRedirectUris_IsInvalidRedirectUri() =>
        Refuse(Valid(redirectUris: [.. Enumerable.Range(1, 6).Select(i => (string?)$"https://claude.ai/cb{i}")]))
            .Error.Should().Be(ClientRegistrationError.InvalidRedirectUri);

    [Theory]
    [InlineData("http://example.com/cb")]
    [InlineData("myapp://cb")]
    [InlineData("https://claude.ai/cb#x")]
    [InlineData(null)]
    public void OneForbiddenRedirectUri_IsInvalidRedirectUri(string? uri) =>
        Refuse(Valid(redirectUris: ["https://claude.ai/ok", uri])).Error.Should().Be(ClientRegistrationError.InvalidRedirectUri);

    /// <summary>
    /// A host in letters that can imitate another one (Cyrillic с, full-width ｃ…), or that IDNA
    /// rejects altogether: only the punycode form is accepted — the form the consent screen shows.
    /// </summary>
    [Theory]
    [InlineData("https://\u0441laude.ai/cb")]
    [InlineData("https://\uFF43laude.ai/cb")]
    [InlineData("https://claude.ai\u3002evil.example/cb")]
    [InlineData("https://\uFFFD.example/cb")]
    [InlineData("https://a\u200Db.example/cb")]
    [InlineData("https://claude.ai/caf\u00e9")]
    public void NonAsciiRedirectUri_IsInvalidRedirectUri(string uri) =>
        Refuse(Valid(redirectUris: [uri])).Error.Should().Be(ClientRegistrationError.InvalidRedirectUri);

    [Theory]
    [InlineData("https://xn--laude-0ye.ai/cb")]
    [InlineData("https://xn--mnchen-3ya.de/cb")]
    public void PunycodeHost_IsAccepted(string uri) =>
        Accept(Valid(redirectUris: [uri])).RedirectUris.Should().Equal(uri);

    /// <summary>A client may not pass for HouseFlow on the consent screen, nor have codes sent to it.</summary>
    [Theory]
    [InlineData("https://api.houseflow.app/cb")]
    [InlineData("https://www.houseflow.app/oauth/consent")]
    [InlineData("https://WWW.HouseFlow.app:8443/cb")]
    [InlineData("https://www.houseflow.app./cb")]
    [InlineData("http://localhost:3000/cb")]
    [InlineData("http://127.0.0.1:3000/cb")]
    [InlineData("http://[::1]:5203/cb")]
    public void RedirectUriOnHouseFlowItself_IsInvalidRedirectUri(string uri) =>
        Refuse(Valid(redirectUris: ["https://claude.ai/cb", uri])).Error.Should().Be(ClientRegistrationError.InvalidRedirectUri);

    /// <summary>Another host — a subdomain included — and, on loopback, another port: someone else's.</summary>
    [Theory]
    [InlineData("https://claude.houseflow.app/cb")]
    [InlineData("https://houseflow.app.evil.example/cb")]
    [InlineData("http://localhost:8080/cb")]
    [InlineData("http://127.0.0.1:9/cb")]
    public void RedirectUriNextToHouseFlow_IsAccepted(string uri) =>
        Accept(Valid(redirectUris: [uri])).RedirectUris.Should().Equal(uri);

    [Theory]
    [InlineData("client_secret_basic")]
    [InlineData("client_secret_post")]
    [InlineData("private_key_jwt")]
    public void ConfidentialClient_IsInvalidClientMetadata(string method) =>
        Refuse(Valid(authMethod: method)).Error.Should().Be(ClientRegistrationError.InvalidClientMetadata);

    [Theory]
    [InlineData("implicit")]
    [InlineData("password")]
    [InlineData("client_credentials")]
    [InlineData("urn:ietf:params:oauth:grant-type:device_code")]
    public void UnsupportedGrantType_IsInvalidClientMetadata(string grant) =>
        Refuse(Valid(grantTypes: ["authorization_code", grant])).Error.Should().Be(ClientRegistrationError.InvalidClientMetadata);

    [Fact]
    public void GrantTypesWithoutAuthorizationCode_IsInvalidClientMetadata() =>
        Refuse(Valid(grantTypes: ["refresh_token"])).Error.Should().Be(ClientRegistrationError.InvalidClientMetadata);

    [Theory]
    [InlineData("token")]
    [InlineData("id_token")]
    [InlineData("code id_token")]
    public void UnsupportedResponseType_IsInvalidClientMetadata(string responseType) =>
        Refuse(Valid(responseTypes: [responseType])).Error.Should().Be(ClientRegistrationError.InvalidClientMetadata);

    [Theory]
    [InlineData("houses:read admin")]
    [InlineData("openid")]
    [InlineData("offline_access")]
    public void UnknownOrNoHouseFlowScope_IsInvalidClientMetadata(string scope) =>
        Refuse(Valid(scope: scope)).Error.Should().Be(ClientRegistrationError.InvalidClientMetadata);

    [Theory]
    [InlineData("http://claude.ai")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://claude.ai/#x")]
    [InlineData("not a uri")]
    public void ClientUriNotHttps_IsInvalidClientMetadata(string clientUri) =>
        Refuse(Valid(clientUri: clientUri)).Error.Should().Be(ClientRegistrationError.InvalidClientMetadata);
}
