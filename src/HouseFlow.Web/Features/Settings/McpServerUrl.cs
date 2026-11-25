namespace HouseFlow.Web.Features.Settings;

/// <summary>
/// Address of the MCP server (#386), shown in P11 « Connecter Claude ». The server lives on the API's
/// origin at <c>/mcp</c> (<c>OAuthResources.McpPath</c>): it is derived from the API base URL the
/// front already knows, so it follows the API host (production, preview, local) with no call and
/// no hard-coded host. Pure and dependency-free: the unit tests compile this file by link.
/// </summary>
public static class McpServerUrl
{
    /// <summary>Path of the MCP server under the API's origin (same value as the API's <c>OAuthResources.McpPath</c>).</summary>
    public const string Path = "/mcp";

    /// <summary>
    /// <c>https://api.houseflow.cloud/mcp</c> for <c>https://api.houseflow.cloud/</c>: scheme and host in
    /// lowercase, default port dropped, trailing slash ignored, like the server's canonical resource.
    /// Empty when <paramref name="apiBaseUrl"/> is not an absolute http(s) URL.
    /// </summary>
    public static string From(string? apiBaseUrl)
    {
        if (!Uri.TryCreate(apiBaseUrl?.Trim(), UriKind.Absolute, out var api)
            || (api.Scheme != Uri.UriSchemeHttp && api.Scheme != Uri.UriSchemeHttps))
            return string.Empty;

        return api.GetLeftPart(UriPartial.Authority) + api.AbsolutePath.TrimEnd('/') + Path;
    }
}
