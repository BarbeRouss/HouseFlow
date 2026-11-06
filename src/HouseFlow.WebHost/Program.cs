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
//
// Devcontainer (scripts/feature-env.sh → scripts/dev-web.sh): Docker publishes the
// container's :3000/:5203 on dynamic HOST ports, so a browser on the host must call the API
// through a different port than in-container clients (E2E: localhost:3000 → localhost:5203).
// When HOST_WEB_PORT + HOST_API_PORT are set, a request that came in through the published
// web port (its Host header carries that port) gets the published API port, on the same
// hostname; every other request keeps API_BASE_URL. Both unset → unchanged behaviour.
var apiBaseUrl = Environment.GetEnvironmentVariable("API_BASE_URL") ?? "http://localhost:5203";
var demoMode = Environment.GetEnvironmentVariable("DEMO_MODE") ?? "false";
var hostWebPort = int.TryParse(Environment.GetEnvironmentVariable("HOST_WEB_PORT"), out var webPort) ? webPort : (int?)null;
var hostApiPort = int.TryParse(Environment.GetEnvironmentVariable("HOST_API_PORT"), out var apiPort) ? apiPort : (int?)null;

app.MapGet("/appsettings.json", (HttpRequest request) =>
{
    var resolvedApiBaseUrl = hostWebPort is not null && hostApiPort is not null && request.Host.Port == hostWebPort
        ? $"{request.Scheme}://{request.Host.Host}:{hostApiPort}"
        : apiBaseUrl;
    return Results.Json(new { ApiBaseUrl = resolvedApiBaseUrl, DemoMode = demoMode },
        new JsonSerializerOptions { PropertyNamingPolicy = null });
});

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();
