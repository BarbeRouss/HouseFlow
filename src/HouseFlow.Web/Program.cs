using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using BlazorBlueprint.Components;
using BlazorBlueprint.Primitives.Services;
using HouseFlow.Web;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Populated at build time from wwwroot/appsettings.json (see the WriteRuntimeConfig
// MSBuild target in HouseFlow.Web.csproj), which is regenerated from the API_BASE_URL
// environment variable AppHost sets for this project — the API's port is only known
// at run time (Aspire assigns it dynamically per environment/worktree).
var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5203";

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(apiBaseUrl) });
builder.Services.AddBlazorBlueprintComponents();

await builder.Build().RunAsync();
