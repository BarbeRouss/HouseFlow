using Aspire.Hosting;
using Aspire.Hosting.Testing;
using HouseFlow.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
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
        // POSTGRES_HOST should never be anything other than the devcontainer's compose
        // service name in this codebase (see Program.cs) — refuse to run a destructive
        // DROP DATABASE against anything else, e.g. a stray/misconfigured env var.
        if (postgresHost != "postgres")
        {
            throw new InvalidOperationException(
                $"Refusing to reset the test database: POSTGRES_HOST is '{postgresHost}', expected 'postgres' (the devcontainer's Postgres sidecar).");
        }

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

    /// <summary>
    /// Opens a <see cref="HouseFlowDbContext"/> on the same database the API under test
    /// uses. Needed by tests that have to seed back-dated rows or run an Infrastructure
    /// job directly (e.g. <c>DataRetentionJob</c>): those rely on
    /// <c>ExecuteUpdate</c>/<c>ExecuteDelete</c>, which the InMemory provider used by the
    /// unit tests does not support, so they must run against real PostgreSQL.
    /// The caller owns the returned context and must dispose it.
    /// </summary>
    public async Task<HouseFlowDbContext> CreateDbContextAsync()
    {
        // The schema is created by dbContext.Database.Migrate() at API startup, which runs
        // before Kestrel starts listening. A test that only touches the database (never the
        // HTTP API) would otherwise race the migration and hit "relation does not exist".
        await WaitForApiAsync();

        var connectionString = await _app!.GetConnectionStringAsync("houseflow")
            ?? throw new InvalidOperationException("No 'houseflow' connection string exposed by the AppHost.");

        var options = new DbContextOptionsBuilder<HouseFlowDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new HouseFlowDbContext(options);
    }

    private readonly SemaphoreSlim _apiReadyGate = new(1, 1);
    private bool _apiReady;

    private async Task WaitForApiAsync()
    {
        if (_apiReady) return;

        await _apiReadyGate.WaitAsync();
        try
        {
            if (_apiReady) return;

            using var client = CreateApiClient();
            for (var attempt = 0; attempt < 60; attempt++)
            {
                try
                {
                    var response = await client.GetAsync("/alive");
                    if (response.IsSuccessStatusCode)
                    {
                        _apiReady = true;
                        return;
                    }
                }
                catch (HttpRequestException)
                {
                    // API not listening yet.
                }

                await Task.Delay(500);
            }

            throw new InvalidOperationException("The API never became reachable; the database schema may not be migrated.");
        }
        finally
        {
            _apiReadyGate.Release();
        }
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
