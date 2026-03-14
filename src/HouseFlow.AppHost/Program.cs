IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

// Docker Compose publisher for deployment
builder.AddDockerComposeEnvironment("houseflow");

// Add PostgreSQL server with persistent volume
var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin()
    .WithDataVolume();

// Add the database
var houseflowDb = postgres.AddDatabase("houseflow");

// Add the API project with database reference
var api = builder.AddProject("api", "../HouseFlow.API/HouseFlow.API.csproj")
    .WithReference(houseflowDb)
    .WaitFor(houseflowDb)
    .WithHttpEndpoint(port: 5203, env: "PORT")
    .WithExternalHttpEndpoints();

// Add the Frontend (Next.js) with API reference
var frontend = builder.AddNpmApp("frontend", "../HouseFlow.Frontend", "dev")
    .WithReference(api)
    .WaitFor(api)
    .WithHttpEndpoint(port: 3000, env: "PORT")
    .WithExternalHttpEndpoints()
    .PublishAsDockerFile();

builder.Build().Run();
