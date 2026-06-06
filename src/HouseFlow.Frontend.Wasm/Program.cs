using HouseFlow.Frontend.Wasm;
using HouseFlow.Frontend.Wasm.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Radzen;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Runtime config (wwwroot/appsettings.json) — keeps the static build environment-agnostic.
var apiBaseUrl = (builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5203/api/v1").TrimEnd('/');
builder.Services.AddSingleton(new AppConfig { ApiBaseUrl = apiBaseUrl });

// Auth + token plumbing (mirrors the Next.js axios client).
builder.Services.AddScoped<TokenStore>();
builder.Services.AddTransient<BearerHandler>();
builder.Services.AddScoped<HouseFlowAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<HouseFlowAuthStateProvider>());
builder.Services.AddAuthorizationCore();

// Typed API clients sharing the base address + bearer/refresh handler.
builder.Services.AddHttpClient<AuthService>(c => c.BaseAddress = new Uri($"{apiBaseUrl}/"))
    .AddHttpMessageHandler<BearerHandler>();
builder.Services.AddHttpClient<HousesService>(c => c.BaseAddress = new Uri($"{apiBaseUrl}/"))
    .AddHttpMessageHandler<BearerHandler>();

// Radzen UI services.
builder.Services.AddRadzenComponents();

await builder.Build().RunAsync();
