using System.Text.Json;
using FluentAssertions;
using HouseFlow.Application.OAuth;

namespace HouseFlow.UnitTests.OAuth;

public class ClientMetadataDocumentValidatorTests
{
    private const string ClientId = "https://claude.ai/.well-known/oauth-client-metadata";
    private static readonly Uri Url = new(ClientId);
    private static readonly Uri[] HouseFlowOrigins = [new("https://api.houseflow.app"), new("https://www.houseflow.app")];

    private static ClientIdMetadataDocument Document(
        string? clientId = ClientId,
        string? name = "Claude",
        List<string?>? redirectUris = null,
        string? authMethod = null) => new()
    {
        ClientId = clientId,
        ClientName = name,
        RedirectUris = redirectUris ?? ["https://claude.ai/api/mcp/auth_callback"],
        TokenEndpointAuthMethod = authMethod
    };

    private static string? Refuse(ClientIdMetadataDocument? document)
    {
        ClientMetadataDocumentValidator.TryValidate(Url, document, HouseFlowOrigins, out var registration, out var error)
            .Should().BeFalse();
        registration.Should().BeNull();
        return error;
    }

    [Fact]
    public void Valid_BecomesAPublicClientRegistration()
    {
        ClientMetadataDocumentValidator.TryValidate(Url, Document(authMethod: "none"), HouseFlowOrigins, out var registration, out var error)
            .Should().BeTrue(error);

        registration!.ClientName.Should().Be("Claude");
        registration.RedirectUris.Should().Equal("https://claude.ai/api/mcp/auth_callback");
        registration.AllowsRefreshTokens.Should().BeTrue("the grant types default like a dynamic registration");
        registration.Scopes.Should().Equal("houses:read", "houses:write");
    }

    [Fact]
    public void Refused_WhenTheDocumentIsNotAnObject() => Refuse(null).Should().Contain("JSON object");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://claude.ai/other.json")]
    [InlineData("https://evil.example/.well-known/oauth-client-metadata")]
    [InlineData("HTTPS://CLAUDE.AI/.well-known/oauth-client-metadata")]
    public void Refused_WhenItsClientIdIsNotTheUrlItWasFetchedAt(string? clientId) =>
        Refuse(Document(clientId)).Should().Contain("client_id");

    [Theory]
    [InlineData("client_secret_basic")]
    [InlineData("client_secret_post")]
    [InlineData("private_key_jwt")]
    public void Refused_WhenTheClientIsNotPublic(string method) =>
        Refuse(Document(authMethod: method)).Should().Contain("public");

    [Fact]
    public void Refused_WhenARedirectUriIsOnAnotherHost() =>
        Refuse(Document(redirectUris: ["https://evil.example/cb"])).Should().Contain("host of the client_id");

    [Fact]
    public void Refused_WhenOnlyOneRedirectUriIsOnAnotherHost() =>
        Refuse(Document(redirectUris: ["https://claude.ai/cb", "https://auth.claude.ai/cb"])).Should().Contain("host of the client_id");

    [Theory]
    [InlineData("https://claude.ai.evil.example/cb")]
    [InlineData("https://notclaude.ai/cb")]
    [InlineData("https://claude.ai:8443/cb")]
    public void Refused_WhenTheHostOnlyLooksTheSame(string redirectUri) =>
        Refuse(Document(redirectUris: [redirectUri])).Should().Contain("host of the client_id");

    [Theory]
    [InlineData("http://claude.ai/cb")]
    [InlineData("myapp://callback")]
    [InlineData("https://claude.ai/cb#frag")]
    public void Refused_WhenARedirectUriBreaksTheRegistrationRules(string redirectUri) =>
        Refuse(Document(redirectUris: [redirectUri])).Should().NotBeNull();

    [Fact]
    public void Refused_WhenARedirectUriPointsToHouseFlow()
    {
        var url = new Uri("https://www.houseflow.app/client.json");
        ClientMetadataDocumentValidator.TryValidate(
                url,
                new ClientIdMetadataDocument { ClientId = url.OriginalString, ClientName = "Fake", RedirectUris = ["https://www.houseflow.app/cb"] },
                HouseFlowOrigins, out _, out var error)
            .Should().BeFalse();
        error.Should().Contain("HouseFlow itself");
    }

    [Theory]
    [InlineData("http://127.0.0.1:9/callback")]
    [InlineData("http://localhost:3000/cb")]
    [InlineData("http://[::1]:8080/cb")]
    public void LoopbackRedirectUri_StaysAdmitted(string loopback)
    {
        ClientMetadataDocumentValidator.TryValidate(
                Url, Document(redirectUris: ["https://claude.ai/cb", loopback]), HouseFlowOrigins, out var registration, out var error)
            .Should().BeTrue(error);
        registration!.RedirectUris.Should().Contain(loopback);
    }

    // Documents served by real clients, copied as fetched (2026-11-13), parsed like the fetcher does.
    private const string ClaudeClientId = "https://claude.ai/oauth/mcp-oauth-client-metadata";
    private const string ClaudeDocument =
        """{"client_id":"https://claude.ai/oauth/mcp-oauth-client-metadata","client_name":"Claude","client_uri":"https://claude.ai","redirect_uris":["https://claude.ai/api/mcp/auth_callback"],"grant_types":["authorization_code","refresh_token","urn:ietf:params:oauth:grant-type:jwt-bearer"],"response_types":["code"],"token_endpoint_auth_method":"none"}""";
    private const string VsCodeClientId = "https://vscode.dev/oauth/client-metadata.json";
    private const string VsCodeDocument =
        """{"client_name":"Visual Studio Code","logo_uri":"https://code.visualstudio.com/assets/branding/code-stable.png","grant_types":["authorization_code","refresh_token","urn:ietf:params:oauth:grant-type:device_code"],"response_types":["code"],"token_endpoint_auth_method":"none","application_type":"native","client_id":"https://vscode.dev/oauth/client-metadata.json","client_uri":"https://vscode.dev/product","redirect_uris":["http://127.0.0.1:33418/","https://vscode.dev/redirect"]}""";

    private static bool Validate(string clientId, string json, out ValidatedClientRegistration? registration, out string? error) =>
        ClientMetadataDocumentValidator.TryValidate(
            new Uri(clientId), JsonSerializer.Deserialize<ClientIdMetadataDocument>(json), HouseFlowOrigins, out registration, out error);

    [Fact]
    public void RealClaudeDocument_IsAccepted_UnhandledGrantTypesIgnored()
    {
        Validate(ClaudeClientId, ClaudeDocument, out var registration, out var error).Should().BeTrue(error);

        registration!.ClientName.Should().Be("Claude");
        registration.GrantTypes.Should().Equal("authorization_code", "refresh_token");
        registration.AllowsRefreshTokens.Should().BeTrue();
        registration.RedirectUris.Should().Equal("https://claude.ai/api/mcp/auth_callback");
    }

    [Fact]
    public void RealVsCodeDocument_IsAccepted_UnhandledGrantTypesIgnored()
    {
        Validate(VsCodeClientId, VsCodeDocument, out var registration, out var error).Should().BeTrue(error);

        registration!.GrantTypes.Should().Equal("authorization_code", "refresh_token");
        registration.RedirectUris.Should().Contain("https://vscode.dev/redirect");
    }

    [Fact]
    public void Refused_WhenOnlyUnhandledGrantTypesAreDeclared()
    {
        var json = ClaudeDocument.Replace("\"authorization_code\",\"refresh_token\",", string.Empty);

        Validate(ClaudeClientId, json, out var registration, out var error).Should().BeFalse();
        registration.Should().BeNull();
        error.Should().Contain("authorization_code");
    }

    [Fact]
    public void Refused_WhenAuthorizationCodeIsMissingFromTheGrantTypes()
    {
        var json = ClaudeDocument.Replace("\"authorization_code\",", string.Empty);

        Validate(ClaudeClientId, json, out _, out var error).Should().BeFalse();
        error.Should().Contain("authorization_code");
    }

    [Fact]
    public void Refresh_IsNotGranted_WhenTheDocumentDoesNotDeclareIt()
    {
        var json = ClaudeDocument.Replace("\"refresh_token\",", string.Empty);

        Validate(ClaudeClientId, json, out var registration, out var error).Should().BeTrue(error);
        registration!.GrantTypes.Should().Equal("authorization_code");
        registration.AllowsRefreshTokens.Should().BeFalse();
    }

    [Fact]
    public void Refused_WhenTheNameCouldLie() =>
        Refuse(Document(name: "Claude\u202E")).Should().Contain("client_name");
}

public class ClientMetadataCachePolicyTests
{
    [Fact]
    public void WithoutMaxAge_IsOneHour() => ClientMetadataCachePolicy.Ttl(null).Should().Be(TimeSpan.FromHours(1));

    [Theory]
    [InlineData(0, 5 * 60)]
    [InlineData(30, 5 * 60)]
    [InlineData(5 * 60, 5 * 60)]
    [InlineData(20 * 60, 20 * 60)]
    [InlineData(60 * 60, 60 * 60)]
    [InlineData(24 * 60 * 60, 60 * 60)]
    public void MaxAge_IsBoundedBetweenFiveMinutesAndOneHour(int maxAgeSeconds, int expectedSeconds) =>
        ClientMetadataCachePolicy.Ttl(TimeSpan.FromSeconds(maxAgeSeconds)).Should().Be(TimeSpan.FromSeconds(expectedSeconds));
}
