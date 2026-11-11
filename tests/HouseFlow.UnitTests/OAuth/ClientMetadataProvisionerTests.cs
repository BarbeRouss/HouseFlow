using System.Collections.Immutable;
using System.Text.Json;
using FluentAssertions;
using HouseFlow.API.OAuth;
using HouseFlow.Application.OAuth;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace HouseFlow.UnitTests.OAuth;

/// <summary>
/// A URL <c>client_id</c> becomes an OpenIddict application (#308): created from its document,
/// cached in the row, refreshed past its TTL, refused — never served stale — when it cannot be
/// refreshed. The OpenIddict manager is mocked; the full HTTP flow cannot run through the Aspire
/// test host (the API runs in its own process and, rightly, refuses to download from loopback).
/// </summary>
public class ClientMetadataProvisionerTests
{
    private const string ClientId = "https://claude.ai/.well-known/oauth-client-metadata";
    private static readonly Uri[] Reserved = [new("https://api.houseflow.app")];

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeFetcher : IClientMetadataDocumentFetcher
    {
        public ClientMetadataFetchResult Result { get; set; } = ClientMetadataFetchResult.Success(Document(), null);
        public int Calls { get; private set; }
        public string? LastCaller { get; private set; }

        public Task<ClientMetadataFetchResult> FetchAsync(Uri url, string callerKey, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastCaller = callerKey;
            return Task.FromResult(Result);
        }
    }

    private static ClientIdMetadataDocument Document(
        string clientId = ClientId, string name = "Claude", params string[] redirectUris) => new()
    {
        ClientId = clientId,
        ClientName = name,
        RedirectUris = [.. (redirectUris.Length == 0 ? ["https://claude.ai/api/mcp/auth_callback"] : redirectUris)]
    };

    private sealed class Harness
    {
        public Clock Clock { get; } = new(DateTimeOffset.Parse("2026-11-11T12:00:00Z"));
        public FakeFetcher Fetcher { get; } = new();
        public Mock<IOpenIddictApplicationManager> Manager { get; } = new();
        public object? Stored { get; set; }
        public ImmutableDictionary<string, JsonElement> StoredProperties { get; set; } = ImmutableDictionary<string, JsonElement>.Empty;
        public List<OpenIddictApplicationDescriptor> Created { get; } = [];
        public List<OpenIddictApplicationDescriptor> Updated { get; } = [];

        public Harness()
        {
            Manager.Setup(m => m.FindByClientIdAsync(ClientId, It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<object?>(Stored));
            Manager.Setup(m => m.GetPropertiesAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<ImmutableDictionary<string, JsonElement>>(StoredProperties));
            Manager.Setup(m => m.CreateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()))
                .Returns((OpenIddictApplicationDescriptor descriptor, CancellationToken _) =>
                {
                    Created.Add(descriptor);
                    Stored = "application";
                    return new ValueTask<object>("application");
                });
            Manager.Setup(m => m.PopulateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .Callback((OpenIddictApplicationDescriptor descriptor, object _, CancellationToken _) =>
                {
                    foreach (var (name, value) in StoredProperties) descriptor.Properties[name] = value;
                    descriptor.ClientId = ClientId;
                    descriptor.DisplayName = "Old name";
                    descriptor.RedirectUris.Add(new Uri("https://claude.ai/old"));
                    descriptor.Permissions.Add(Permissions.Prefixes.Scope + "old");
                })
                .Returns(ValueTask.CompletedTask);
            Manager.Setup(m => m.UpdateAsync(It.IsAny<object>(), It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()))
                .Callback((object _, OpenIddictApplicationDescriptor descriptor, CancellationToken _) => Updated.Add(descriptor))
                .Returns(ValueTask.CompletedTask);
        }

        public ClientMetadataProvisioner Provisioner =>
            new(Manager.Object, Fetcher, Clock, NullLogger<ClientMetadataProvisioner>.Instance);

        public Task<ClientMetadataOutcome> EnsureAsync(string clientId = ClientId) =>
            Provisioner.EnsureAsync(clientId, Reserved, "203.0.113.7", CancellationToken.None);

        /// <summary>The application is stored, its document trusted for <paramref name="remaining"/> more.</summary>
        public void HasStoredApplication(TimeSpan remaining, bool cimd = true)
        {
            Stored = "application";
            var properties = ImmutableDictionary<string, JsonElement>.Empty;
            if (cimd)
                properties = properties.Add(ClientMetadataProvisioner.ClientIdMetadataProperty, JsonSerializer.SerializeToElement(true));
            properties = properties.Add(ClientMetadataProvisioner.ExpiresAtProperty,
                JsonSerializer.SerializeToElement((Clock.Now + remaining).ToUnixTimeSeconds()));
            StoredProperties = properties;
        }
    }

    [Fact]
    public async Task FirstAuthorization_CreatesThePublicClientFromItsDocument()
    {
        var h = new Harness();

        var outcome = await h.EnsureAsync();

        outcome.Succeeded.Should().BeTrue(outcome.Description);
        h.Fetcher.Calls.Should().Be(1);
        h.Fetcher.LastCaller.Should().Be("203.0.113.7");
        var created = h.Created.Should().ContainSingle().Subject;
        created.ClientId.Should().Be(ClientId);
        created.DisplayName.Should().Be("Claude");
        created.ClientType.Should().Be(ClientTypes.Public);
        created.ConsentType.Should().Be(ConsentTypes.Explicit);
        created.ApplicationType.Should().Be(ApplicationTypes.Web);
        created.Requirements.Should().Contain(Requirements.Features.ProofKeyForCodeExchange);
        created.RedirectUris.Should().Equal(new Uri("https://claude.ai/api/mcp/auth_callback"));
        created.Permissions.Should().Contain([
            Permissions.GrantTypes.AuthorizationCode, Permissions.GrantTypes.RefreshToken, Permissions.ResponseTypes.Code,
            Permissions.Endpoints.Authorization, Permissions.Endpoints.Token,
            Permissions.Prefixes.Scope + "houses:read", Permissions.Prefixes.Scope + "houses:write"
        ]);
        created.Properties[ClientMetadataProvisioner.ClientIdMetadataProperty].GetBoolean().Should().BeTrue();
        created.Properties[ClientMetadataProvisioner.FetchedAtProperty].GetInt64().Should().Be(h.Clock.Now.ToUnixTimeSeconds());
        created.Properties[ClientMetadataProvisioner.ExpiresAtProperty].GetInt64().Should().Be((h.Clock.Now + TimeSpan.FromHours(1)).ToUnixTimeSeconds());
    }

    [Fact]
    public async Task TheStoredCopyLasts_AsLongAsTheDocumentSaysWithinTheBounds()
    {
        var h = new Harness();
        h.Fetcher.Result = ClientMetadataFetchResult.Success(Document(), TimeSpan.FromSeconds(10));

        await h.EnsureAsync();

        h.Created.Single().Properties[ClientMetadataProvisioner.ExpiresAtProperty].GetInt64()
            .Should().Be((h.Clock.Now + TimeSpan.FromMinutes(5)).ToUnixTimeSeconds());
    }

    [Fact]
    public async Task WithinTheTtl_NothingIsDownloaded()
    {
        var h = new Harness();
        h.HasStoredApplication(remaining: TimeSpan.FromMinutes(30));

        var outcome = await h.EnsureAsync();

        outcome.Succeeded.Should().BeTrue();
        h.Fetcher.Calls.Should().Be(0);
        h.Created.Should().BeEmpty();
        h.Updated.Should().BeEmpty();
    }

    [Fact]
    public async Task PastTheTtl_TheDocumentIsDownloadedAgain_AndTheApplicationReplacedByIt()
    {
        var h = new Harness();
        h.HasStoredApplication(remaining: TimeSpan.FromMinutes(-1));
        h.Fetcher.Result = ClientMetadataFetchResult.Success(Document(name: "Claude 2", redirectUris: new[] { "https://claude.ai/new" }), null);

        var outcome = await h.EnsureAsync();

        outcome.Succeeded.Should().BeTrue();
        h.Fetcher.Calls.Should().Be(1);
        h.Created.Should().BeEmpty();
        var updated = h.Updated.Should().ContainSingle().Subject;
        updated.DisplayName.Should().Be("Claude 2");
        updated.RedirectUris.Should().BeEquivalentTo([new Uri("https://claude.ai/new")], "a removed redirect URI is gone");
        updated.Permissions.Should().NotContain(Permissions.Prefixes.Scope + "old");
        updated.Properties[ClientMetadataProvisioner.ExpiresAtProperty].GetInt64().Should().Be((h.Clock.Now + TimeSpan.FromHours(1)).ToUnixTimeSeconds());
    }

    [Fact]
    public async Task PastTheTtl_AFailedDownloadRefuses_ItDoesNotServeAStaleCopy()
    {
        var h = new Harness();
        h.HasStoredApplication(remaining: TimeSpan.FromMinutes(-1));
        h.Fetcher.Result = ClientMetadataFetchResult.Failed(ClientMetadataFetchFailure.Unreachable);

        var outcome = await h.EnsureAsync();

        outcome.Succeeded.Should().BeFalse();
        h.Updated.Should().BeEmpty();
    }

    [Fact]
    public async Task PastTheTtl_ADocumentThatBecameInvalidRefuses()
    {
        var h = new Harness();
        h.HasStoredApplication(remaining: TimeSpan.FromMinutes(-1));
        h.Fetcher.Result = ClientMetadataFetchResult.Success(Document(redirectUris: new[] { "https://evil.example/cb" }), null);

        var outcome = await h.EnsureAsync();

        outcome.Succeeded.Should().BeFalse();
        h.Updated.Should().BeEmpty();
    }

    [Fact]
    public async Task ADocumentThatCannotBeFetched_CreatesNothing()
    {
        var h = new Harness();
        h.Fetcher.Result = ClientMetadataFetchResult.Failed(ClientMetadataFetchFailure.NonPublicAddress);

        var outcome = await h.EnsureAsync();

        outcome.Succeeded.Should().BeFalse();
        outcome.Description.Should().NotBeNullOrEmpty();
        h.Created.Should().BeEmpty();
    }

    [Theory]
    [InlineData("""{"client_id":"https://claude.ai/other.json","client_name":"X","redirect_uris":["https://claude.ai/cb"]}""")]
    [InlineData("""{"client_id":"https://claude.ai/.well-known/oauth-client-metadata","client_name":"X","redirect_uris":["https://claude.ai/cb"],"token_endpoint_auth_method":"client_secret_basic"}""")]
    [InlineData("""{"client_id":"https://claude.ai/.well-known/oauth-client-metadata","client_name":"X","redirect_uris":["https://evil.example/cb"]}""")]
    public async Task AnInvalidDocument_CreatesNothing(string json)
    {
        var h = new Harness();
        h.Fetcher.Result = ClientMetadataFetchResult.Success(JsonSerializer.Deserialize<ClientIdMetadataDocument>(json)!, null);

        var outcome = await h.EnsureAsync();

        outcome.Succeeded.Should().BeFalse();
        h.Created.Should().BeEmpty();
    }

    [Theory]
    [InlineData("http://claude.ai/client.json")]
    [InlineData("https://127.0.0.1/client.json")]
    [InlineData("https://claude.ai:8443/client.json")]
    [InlineData("https://claude.ai/client.json#x")]
    public async Task AClientIdThatIsNotAValidUrl_IsRefusedWithoutAnyDownload(string clientId)
    {
        var h = new Harness();

        var outcome = await h.EnsureAsync(clientId);

        outcome.Succeeded.Should().BeFalse();
        h.Fetcher.Calls.Should().Be(0);
    }

    [Fact]
    public async Task AnApplicationThatIsNotAMetadataDocumentClient_IsNeverRefreshed()
    {
        var h = new Harness();
        h.HasStoredApplication(remaining: TimeSpan.FromMinutes(-1), cimd: false);

        var outcome = await h.EnsureAsync();

        outcome.Succeeded.Should().BeFalse();
        h.Fetcher.Calls.Should().Be(0);
        h.Updated.Should().BeEmpty();
    }

    [Fact]
    public async Task ARaceOnTheUniqueClientId_ReadsTheApplicationTheOtherRequestCreated()
    {
        var h = new Harness();
        h.Manager.Setup(m => m.CreateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                h.Stored = "application"; // created by the concurrent request
                throw new OpenIddictExceptions.ValidationException("duplicate client_id", []);
            });

        var outcome = await h.EnsureAsync();

        outcome.Succeeded.Should().BeTrue();
        h.Updated.Should().ContainSingle("the application of the other request is brought up to date");
    }

    [Fact]
    public void TheHandler_RunsBeforeOpenIddictLooksTheClientUp() =>
        ClientMetadataDocumentHandler.Descriptor.Order.Should()
            .BeLessThan(OpenIddictServerHandlers.ValidateClientId.Descriptor.Order)
            .And.BeGreaterThan(OpenIddictServerHandlers.Authentication.ValidateClientIdParameter.Descriptor.Order);

    [Fact]
    public void LoopbackOnlyDocument_IsANativeClient()
    {
        var descriptor = new OpenIddictApplicationDescriptor();
        ClientMetadataDocumentValidator.TryValidate(
            new Uri(ClientId), Document(redirectUris: new[] { "http://127.0.0.1:9/cb" }), Reserved, out var registration, out _).Should().BeTrue();

        OAuthClientDescriptors.Apply(descriptor, registration!);

        descriptor.ApplicationType.Should().Be(ApplicationTypes.Native);
    }
}

/// <summary>The consent screen shows an origin as verified only when the request goes back to the client_id's host (#308).</summary>
public class VerifiedClientOriginTests
{
    private const string ClientId = "https://claude.ai/.well-known/oauth-client-metadata";
    private const string Callback = "https://claude.ai/api/mcp/auth_callback";

    private static async Task<string?> OriginAsync(
        string clientId, bool cimd, string? requestRedirectUri, bool registeredRedirectUri = true)
    {
        var applications = new Mock<IOpenIddictApplicationManager>();
        var application = new object();
        var properties = ImmutableDictionary<string, JsonElement>.Empty;
        if (cimd)
            properties = properties.Add(ClientMetadataProvisioner.ClientIdMetadataProperty, JsonSerializer.SerializeToElement(true));

        applications.Setup(m => m.GetClientIdAsync(application, It.IsAny<CancellationToken>())).Returns(new ValueTask<string?>(clientId));
        applications.Setup(m => m.GetDisplayNameAsync(application, It.IsAny<CancellationToken>())).Returns(new ValueTask<string?>("Claude"));
        applications.Setup(m => m.GetPropertiesAsync(application, It.IsAny<CancellationToken>())).Returns(new ValueTask<ImmutableDictionary<string, JsonElement>>(properties));
        applications.Setup(m => m.GetRedirectUrisAsync(application, It.IsAny<CancellationToken>())).Returns(new ValueTask<ImmutableArray<string>>([Callback]));
        applications.Setup(m => m.GetPermissionsAsync(application, It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<ImmutableArray<string>>([Permissions.Prefixes.Scope + "houses:read"]));
        applications.Setup(m => m.ValidateRedirectUriAsync(application, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<bool>(registeredRedirectUri));

        var service = new OAuthConsentService(
            applications.Object, Mock.Of<IOpenIddictAuthorizationManager>(), Mock.Of<IOpenIddictTokenManager>(),
            NullLogger<OAuthConsentService>.Instance);

        return (await service.DescribeClientAsync(application, requestRedirectUri)).VerifiedClientOrigin;
    }

    [Fact]
    public async Task MetadataDocumentClient_RedirectedToItsOwnHost_IsVerified() =>
        (await OriginAsync(ClientId, cimd: true, Callback)).Should().Be("claude.ai");

    [Fact]
    public async Task WithoutTheRequestsRedirectUri_NothingIsVerified() =>
        (await OriginAsync(ClientId, cimd: true, requestRedirectUri: null)).Should().BeNull();

    [Fact]
    public async Task DynamicallyRegisteredClient_IsNeverVerified() =>
        (await OriginAsync("0123456789abcdef0123456789abcdef", cimd: false, Callback)).Should().BeNull();

    [Fact]
    public async Task AClientIdThatLooksLikeAUrlButIsNotAMetadataDocumentClient_IsNotVerified() =>
        (await OriginAsync(ClientId, cimd: false, Callback)).Should().BeNull();

    [Theory]
    [InlineData("http://127.0.0.1:9/callback")]
    [InlineData("http://localhost:3000/cb")]
    [InlineData("http://[::1]:8080/cb")]
    public async Task LoopbackRedirection_ProvesNothing(string loopback) =>
        (await OriginAsync(ClientId, cimd: true, loopback)).Should().BeNull();

    [Fact]
    public async Task ARedirectionToAnotherHost_IsNotVerified() =>
        (await OriginAsync(ClientId, cimd: true, "https://evil.example/cb")).Should().BeNull();

    [Fact]
    public async Task ARedirectUriTheClientNeverRegistered_IsNotVerified() =>
        (await OriginAsync(ClientId, cimd: true, Callback, registeredRedirectUri: false)).Should().BeNull();
}
