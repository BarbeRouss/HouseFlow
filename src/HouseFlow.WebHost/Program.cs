using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Serves HouseFlow.Web's (referenced project) compiled wwwroot/_framework output via
// its static web assets manifest instead of a physically-copied wwwroot folder — needed
// because Aspire's AddProject runs this via `dotnet run` without a launchSettings.json,
// so ASPNETCORE_ENVIRONMENT defaults to Production, where this isn't wired up automatically.
// In the published Docker image there is no manifest and the WASM output sits physically
// in wwwroot (see Dockerfile), so this is a no-op there.
builder.WebHost.UseStaticWebAssets();

var app = builder.Build();

// Runtime config read by the WASM app at boot (HouseFlow.Web/Program.cs). Served from the
// host's environment so ONE image works for every environment; Aspire sets API_BASE_URL +
// DEMO_MODE on this host. No deployed environment goes through here any more: the frontend
// is served by a Static Web App, which has no process to override anything, so the deployed
// config is the appsettings.json file itself, written at deploy time by
// scripts/ci/write-runtime-config.sh. This host remains the local-dev and pr.yml path.
// Defaults mirror the WriteRuntimeConfig MSBuild target (local dev). Being an endpoint,
// it takes precedence over the appsettings.json file sitting in wwwroot.
// Keys are PascalCase on purpose: the WASM app reads them with a case-sensitive
// JsonDocument lookup, so the default camelCase policy would silently break it.
app.MapGet("/appsettings.json", () => Results.Json(new
{
    ApiBaseUrl = Environment.GetEnvironmentVariable("API_BASE_URL") ?? "http://localhost:5203",
    DemoMode = Environment.GetEnvironmentVariable("DEMO_MODE") ?? "false",
}, new JsonSerializerOptions { PropertyNamingPolicy = null }));

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();
