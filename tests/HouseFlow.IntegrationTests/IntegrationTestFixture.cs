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
    /// Creates a new HttpClient targeting the API service.
    /// Use this instead of ApiClient when tests need isolated client state
    /// (e.g., setting DefaultRequestHeaders.Authorization per user).
    /// </summary>
    public HttpClient CreateApiClient() => _app!.CreateHttpClient("api");

    public async Task DisposeAsync()
    {
        ApiClient?.Dispose();
        if (_app != null)
            await _app.DisposeAsync();
    }
}

[CollectionDefinition("Integration")]
public class IntegrationCollection : ICollectionFixture<IntegrationTestFixture> { }
