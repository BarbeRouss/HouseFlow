IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

// Docker Compose publisher for deployment (publish mode only)
if (builder.ExecutionContext.IsPublishMode)
{
    builder.AddDockerComposeEnvironment("houseflow");
}

// Add PostgreSQL server with persistent volume
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume();

// PgAdmin only in interactive development (not in tests or CI)
if (!string.Equals(builder.Configuration["SkipFrontend"], "true", StringComparison.OrdinalIgnoreCase))
{
    postgres.WithPgAdmin();
}

// Add the database
var houseflowDb = postgres.AddDatabase("houseflow");

// Add the API project with database reference
var api = builder.AddProject("api", "../HouseFlow.API/HouseFlow.API.csproj")
    .WithReference(houseflowDb)
    .WaitFor(houseflowDb)
    .WithHttpEndpoint(port: 5203, name: "public", env: "PORT")
    .WithExternalHttpEndpoints();

// Add the Frontend (Next.js) with API reference — skipped in integration tests
if (!string.Equals(builder.Configuration["SkipFrontend"], "true", StringComparison.OrdinalIgnoreCase))
{
    builder.AddJavaScriptApp("frontend", "../HouseFlow.Frontend")
        .WithReference(api)
        .WaitFor(api)
        .WithHttpEndpoint(port: 3000, name: "public", env: "PORT")
        .WithExternalHttpEndpoints();
}

builder.Build().Run();
