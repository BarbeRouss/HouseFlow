IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

// Docker Compose publisher for deployment (publish mode only)
if (builder.ExecutionContext.IsPublishMode)
{
    builder.AddDockerComposeEnvironment("houseflow");
}

bool skipFrontend = string.Equals(builder.Configuration["SkipFrontend"], "true", StringComparison.OrdinalIgnoreCase);
string? postgresHost = builder.Configuration["POSTGRES_HOST"];

// In a feature devcontainer, Postgres runs as a dedicated docker-compose sidecar
// (POSTGRES_HOST set via containerEnv) instead of a container Aspire spawns itself via
// the host's Docker socket (which we don't mount — that would be a sibling container,
// not reachable via localhost). This also covers dotnet test / DistributedApplicationTestingBuilder
// when it runs inside the same devcontainer: it gets its own "houseflow_test" database on
// that sidecar, reset once per run by IntegrationTestFixture, instead of Aspire spawning an
// ephemeral Postgres container of its own — which needs Docker access we don't grant here.
// Outside the devcontainer (host, CI), POSTGRES_HOST is unset and behavior is unchanged:
// Aspire spawns its own ephemeral Postgres container for every run, tests included.
IResourceBuilder<IResourceWithConnectionString> houseflowDb;
if (!string.IsNullOrEmpty(postgresHost))
{
    string dbName = skipFrontend ? "houseflow_test" : "houseflow";

    // EF Core's Database.Migrate() (called at API startup) creates the database itself
    // if it doesn't exist yet, so no manual provisioning step is needed here.
    builder.Configuration["ConnectionStrings:houseflow"] =
        $"Host={postgresHost};Port=5432;Database={dbName};Username=postgres;Password=postgres";
    houseflowDb = builder.AddConnectionString("houseflow");
}
else
{
    var postgres = builder.AddPostgres("postgres").WithDataVolume();

    // PgAdmin only in interactive development (not in tests or CI)
    if (!skipFrontend)
    {
        postgres.WithPgAdmin();
    }

    houseflowDb = postgres.AddDatabase("houseflow");
}

// Add the API project with database reference
var demoMode = builder.Configuration["DEMO_MODE"] ?? "false";
var api = builder.AddProject("api", "../HouseFlow.API/HouseFlow.API.csproj")
    .WithReference(houseflowDb)
    .WaitFor(houseflowDb)
    .WithHttpEndpoint(port: 5203, name: "public", env: "PORT")
    .WithExternalHttpEndpoints()
    .WithEnvironment("DEMO_MODE", demoMode);

// Add the Frontend (Blazor WebAssembly, served by a thin ASP.NET Core host —
// HouseFlow.WebHost — rather than orchestrating HouseFlow.Web's dev-only devserver
// directly: that devserver is a generic SDK tool, not a real Kestrel app under our
// control, and DCP's proxy never manages to forward traffic to it (accepts the
// connection, never relays a response — see git history for the investigation).
// The host project behaves like any other ASP.NET Core project (like "api" above),
// which Aspire's proxy handles reliably; it just serves HouseFlow.Web's compiled
// wwwroot output — no server-side rendering, the app still runs 100% client-side.
if (!skipFrontend)
{
    builder.AddProject("frontend", "../HouseFlow.WebHost/HouseFlow.WebHost.csproj")
        .WithReference(api)
        .WaitFor(api)
        .WithHttpEndpoint(port: 3000, name: "public", env: "PORT")
        .WithExternalHttpEndpoints()
        .WithEnvironment("DEMO_MODE", demoMode)
        .WithEnvironment("API_BASE_URL", api.GetEndpoint("public"));
}

builder.Build().Run();
