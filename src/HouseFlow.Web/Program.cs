using System.Text.Json;
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

// Load runtime config (wwwroot/appsettings.json, written by the WriteRuntimeConfig
// MSBuild target from API_BASE_URL / DEMO_MODE) EXPLICITLY: the automatic
// appsettings.json loading of WebAssemblyHostBuilder.CreateDefault gets trimmed out
// of the published (Release) build, so builder.Configuration is empty there and the
// app fell back to localhost:5203 / DemoMode=false. JsonDocument is trim-safe.
var (apiBaseUrl, demoMode) = await LoadRuntimeConfigAsync(builder.HostEnvironment.BaseAddress);

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

// Fetches and parses wwwroot/appsettings.json without the (trimmed-away) config
// providers. Defaults keep local dev working if the file is missing.
static async Task<(string ApiBaseUrl, bool DemoMode)> LoadRuntimeConfigAsync(string baseAddress)
{
    var apiBaseUrl = "http://localhost:5203";
    var demoMode = false;
    try
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseAddress) };
        await using var stream = await http.GetStreamAsync("appsettings.json");
        using var doc = await JsonDocument.ParseAsync(stream);
        var root = doc.RootElement;
        if (root.TryGetProperty("ApiBaseUrl", out var a) && a.ValueKind == JsonValueKind.String)
            apiBaseUrl = a.GetString() ?? apiBaseUrl;
        if (root.TryGetProperty("DemoMode", out var d))
            demoMode = d.ValueKind == JsonValueKind.True ||
                       (d.ValueKind == JsonValueKind.String &&
                        string.Equals(d.GetString(), "true", StringComparison.OrdinalIgnoreCase));
    }
    catch
    {
        // Missing/unreadable config → keep the local-dev defaults.
    }
    return (apiBaseUrl, demoMode);
}
