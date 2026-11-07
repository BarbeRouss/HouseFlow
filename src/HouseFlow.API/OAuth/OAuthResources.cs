namespace HouseFlow.API.OAuth;

/// <summary>
/// Resource servers the authorization server issues tokens for (RFC 8707): the audience of every
/// access token. A token for the MCP server cannot be replayed against anything else — the REST API
/// does not accept it at all (its bearer handler expects its own JWTs).
/// </summary>
public static class OAuthResources
{
    /// <summary>Path of the MCP server (issue #305) under the API's origin.</summary>
    public const string McpPath = "/mcp";

    /// <summary><c>OAuth:Resources</c> if configured, else the MCP endpoint of the API that received the request.</summary>
    public static IReadOnlyList<string> Allowed(HttpRequest request, OAuthOptions options)
    {
        var configured = options.Resources.Select(Normalize).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        if (configured.Count > 0) return configured;

        // The scheme comes from the trusted ingress (forwarded headers, see HttpEdge).
        return [Normalize($"{request.Scheme}://{request.Host}{request.PathBase}{McpPath}")!];
    }

    /// <summary>
    /// The resources a token may be issued for: those requested (all of them allowed), or every
    /// allowed one when none is requested. <paramref name="granted"/> narrows the allowed set to what
    /// an earlier grant covered (token endpoint). <c>null</c> means <c>invalid_target</c>.
    /// </summary>
    public static IReadOnlyList<string>? Resolve(
        IEnumerable<string> requested, IReadOnlyList<string> allowed, IEnumerable<string>? granted = null)
    {
        var permitted = granted is null
            ? allowed.ToList()
            : allowed.Intersect(granted.Select(Normalize).OfType<string>(), StringComparer.Ordinal).ToList();

        var asked = requested.Select(resource => Normalize(resource) ?? resource).Distinct(StringComparer.Ordinal).ToList();
        if (asked.Count == 0)
            return permitted.Count > 0 ? permitted : null;

        return asked.All(resource => permitted.Contains(resource, StringComparer.Ordinal)) ? asked : null;
    }

    /// <summary>Canonical form (scheme and host in lowercase, default port dropped) of an absolute URI.</summary>
    public static string? Normalize(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri.AbsoluteUri : null;
}
