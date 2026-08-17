using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Npgsql;

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
        // Inside a devcontainer, Program.cs points at the shared Postgres sidecar instead of
        // an Aspire-spawned ephemeral container (see Program.cs), using a dedicated
        // "houseflow_test" database so this never touches the interactive dev database. That
        // database persists across runs on the sidecar, so reset it here once per run — the
        // one guarantee an ephemeral container gave us for free. Outside a devcontainer,
        // POSTGRES_HOST is unset and Aspire still spawns a fresh container every run, so no
        // reset is needed there.
        var postgresHost = Environment.GetEnvironmentVariable("POSTGRES_HOST");
        if (!string.IsNullOrEmpty(postgresHost))
        {
            await ResetTestDatabaseAsync(postgresHost);
        }

        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.HouseFlow_AppHost>(["--SkipFrontend=true"]);

        _app = await appHost.BuildAsync();
        await _app.StartAsync();

        ApiClient = _app.CreateHttpClient("api");
    }

    private static async Task ResetTestDatabaseAsync(string postgresHost)
    {
        const string dbName = "houseflow_test";
        var adminConnectionString = $"Host={postgresHost};Port=5432;Username=postgres;Password=postgres;Database=postgres";

        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();

        // Postgres refuses DROP DATABASE while sessions are still attached (e.g. a leftover
        // connection from a previous, ungracefully-terminated run).
        await using (var terminate = connection.CreateCommand())
        {
            terminate.CommandText =
                "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = $1 AND pid <> pg_backend_pid();";
            terminate.Parameters.AddWithValue(dbName);
            await terminate.ExecuteNonQueryAsync();
        }

        await using (var drop = connection.CreateCommand())
        {
            drop.CommandText = $"DROP DATABASE IF EXISTS {dbName};";
            await drop.ExecuteNonQueryAsync();
        }

        // Re-created and migrated automatically by dbContext.Database.Migrate() at API startup.
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
