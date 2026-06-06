namespace HouseFlow.Frontend.Wasm.Services;

/// <summary>
/// Runtime configuration loaded at startup from wwwroot/appsettings.json.
/// Keeps the build environment-agnostic (no rebuild per environment) — US-400.
/// </summary>
public sealed class AppConfig
{
    /// <summary>Base URL of the HouseFlow API, including the /api/v1 prefix.</summary>
    public string ApiBaseUrl { get; set; } = "http://localhost:5203/api/v1";
}
