namespace HouseFlow.Web;

/// <summary>Runtime configuration resolved at startup (see wwwroot/appsettings.json).</summary>
public sealed class AppConfig
{
    public required string ApiBaseUrl { get; init; }

    /// <summary>When true, the login page shows a one-click "demo login" button
    /// (demo@demo.com). Enabled on demo/preview/test environments via DEMO_MODE.</summary>
    public bool DemoMode { get; init; }
}
