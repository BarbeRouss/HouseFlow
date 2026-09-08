var builder = WebApplication.CreateBuilder(args);

// Serves HouseFlow.Web's (referenced project) compiled wwwroot/_framework output via
// its static web assets manifest instead of a physically-copied wwwroot folder — needed
// because Aspire's AddProject runs this via `dotnet run` without a launchSettings.json,
// so ASPNETCORE_ENVIRONMENT defaults to Production, where this isn't wired up automatically.
builder.WebHost.UseStaticWebAssets();

var app = builder.Build();

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();
