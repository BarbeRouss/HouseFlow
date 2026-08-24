using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using BlazorBlueprint.Components;
using HouseFlow.Web;
using HouseFlow.Web.Api;
using HouseFlow.Web.Auth;
using HouseFlow.Web.Localization;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Populated at build time from wwwroot/appsettings.json (see the WriteRuntimeConfig
// MSBuild target in HouseFlow.Web.csproj), which is regenerated from the API_BASE_URL
// environment variable AppHost sets for this project — the API's port is only known
// at run time (Aspire assigns it dynamically per environment/worktree).
var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5203";
// Parse manually rather than GetValue<bool>: the reflection-based TypeConverter it
// relies on is trimmed out of the published (Release) WASM build, so GetValue<bool>
// silently returns false there even when appsettings.json has "DemoMode": "true".
var demoMode = string.Equals(builder.Configuration["DemoMode"], "true", StringComparison.OrdinalIgnoreCase);

builder.Services.AddSingleton(new AppConfig { ApiBaseUrl = apiBaseUrl, DemoMode = demoMode });

// Localization (message catalogs embedded in the assembly).
builder.Services.AddSingleton<Localizer>();
builder.Services.AddScoped<LocalizationState>();

// Authentication. These are registered as singletons because Blazor WebAssembly
// is single-user AND IHttpClientFactory resolves the message handler in its own
// DI scope — a scoped TokenStore/AuthStateProvider would give the handler a
// different instance than the components, so the bearer token set at boot would
// never reach outgoing requests.
builder.Services.AddSingleton<TokenStore>();
builder.Services.AddSingleton<RedirectGuard>();
builder.Services.AddSingleton<AppAuthStateProvider>();
builder.Services.AddSingleton<AuthenticationStateProvider>(sp => sp.GetRequiredService<AppAuthStateProvider>());
builder.Services.AddAuthorizationCore();

// HTTP client with the auth/refresh/retry pipeline.
builder.Services.AddSingleton<RetryState>();
builder.Services.AddScoped<AuthMessageHandler>();
builder.Services.AddHttpClient("api", client => client.BaseAddress = new Uri(apiBaseUrl))
    .AddHttpMessageHandler<AuthMessageHandler>();
builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("api"));
builder.Services.AddScoped<ApiService>();

// App services.
builder.Services.AddScoped<HouseFlow.Web.ThemeService>();
builder.Services.AddBlazorBlueprintComponents();

await builder.Build().RunAsync();
