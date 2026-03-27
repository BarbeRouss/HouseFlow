using Aspire.Hosting;
using Aspire.Hosting.Testing;

namespace HouseFlow.IntegrationTests;

/// <summary>
/// Shared fixture that starts the Aspire AppHost (PostgreSQL + API) once for all integration tests.
/// Uses xUnit Collection Fixture to avoid restarting containers per test class.
/// </summary>
public class IntegrationTestFixture : IAsyncLifetime
{
    private DistributedApplication? _app;

    public HttpClient ApiClient { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.HouseFlow_AppHost>(["--SkipFrontend=true"]);

        _app = await appHost.BuildAsync();
        await _app.StartAsync();

        ApiClient = _app.CreateHttpClient("api");
    }

    /// <summary>
    /// Creates a new HttpClient targeting the API service with its own cookie-free handler.
    /// Each call returns a fully isolated client (no shared cookie container),
    /// so tests don't leak auth state between clients.
    /// </summary>
    public HttpClient CreateApiClient()
    {
        // Get the base address from Aspire's service discovery
        using var discovery = _app!.CreateHttpClient("api");
        var baseAddress = discovery.BaseAddress;

        // Return a client with its own handler — no cookie pooling
        var handler = new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false };
        return new HttpClient(handler) { BaseAddress = baseAddress };
    }

    public async Task DisposeAsync()
    {
        ApiClient?.Dispose();
        if (_app != null)
            await _app.DisposeAsync();
    }
}

[CollectionDefinition("Integration")]
public class IntegrationCollection : ICollectionFixture<IntegrationTestFixture> { }
