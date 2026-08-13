IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

// Docker Compose publisher for deployment (publish mode only)
if (builder.ExecutionContext.IsPublishMode)
{
    builder.AddDockerComposeEnvironment("houseflow");
}

bool skipFrontend = string.Equals(builder.Configuration["SkipFrontend"], "true", StringComparison.OrdinalIgnoreCase);
string? postgresHost = builder.Configuration["POSTGRES_HOST"];

// In the devcontainer, Postgres runs as a docker-compose sidecar (POSTGRES_HOST set via
// containerEnv) instead of a container Aspire spawns itself via the host's Docker socket
// (which we don't mount — that would be a sibling container, not reachable via localhost).
// Each git worktree gets its own database on that shared server, so a schema/migration
// change in one worktree never affects another. skipFrontend always forces the
// AddPostgres branch so dotnet test / DistributedApplicationTestingBuilder (which never
// sets POSTGRES_HOST) keeps its own isolated ephemeral Postgres, even though the env var
// is container-wide and would otherwise leak into that process too.
IResourceBuilder<IResourceWithConnectionString> houseflowDb;
if (!skipFrontend && !string.IsNullOrEmpty(postgresHost))
{
    string? worktreeName = builder.Configuration["WORKTREE_NAME"] ?? DetectWorktreeName(Directory.GetCurrentDirectory());
    string dbName = string.IsNullOrEmpty(worktreeName) ? "houseflow" : $"houseflow_{SanitizeForIdentifier(worktreeName)}";

    // EF Core's Database.Migrate() (called at API startup) creates the database itself
    // if it doesn't exist yet, so no manual provisioning step is needed here — the first
    // run for a new worktree just creates and migrates its own database.
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
int apiPort = int.TryParse(builder.Configuration["API_PORT"], out var configuredApiPort) ? configuredApiPort : 5203;
var api = builder.AddProject("api", "../HouseFlow.API/HouseFlow.API.csproj")
    .WithReference(houseflowDb)
    .WaitFor(houseflowDb)
    .WithHttpEndpoint(port: apiPort, name: "public", env: "PORT")
    .WithExternalHttpEndpoints()
    .WithEnvironment("DEMO_MODE", demoMode);

// Add the Frontend (Next.js) with API reference — skipped in integration tests
if (!skipFrontend)
{
    int frontendPort = int.TryParse(builder.Configuration["FRONTEND_PORT"], out var configuredFrontendPort) ? configuredFrontendPort : 3000;
    builder.AddJavaScriptApp("frontend", "../HouseFlow.Frontend")
        .WithReference(api)
        .WaitFor(api)
        .WithHttpEndpoint(port: frontendPort, name: "public", env: "PORT")
        .WithExternalHttpEndpoints()
        .WithEnvironment("DEMO_MODE", demoMode);
}

builder.Build().Run();

// Looks for a ".claude/worktrees/<name>" segment in the given path and returns <name>,
// so a worktree gets its own database automatically without any manual bookkeeping.
static string? DetectWorktreeName(string path)
{
    var parts = Path.GetFullPath(path).Split(Path.DirectorySeparatorChar);
    for (int i = 0; i < parts.Length - 2; i++)
    {
        if (parts[i] == ".claude" && parts[i + 1] == "worktrees")
        {
            return parts[i + 2];
        }
    }

    return null;
}

static string SanitizeForIdentifier(string value)
{
    var sanitized = new string(value.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_').ToArray());
    return sanitized.Trim('_');
}
