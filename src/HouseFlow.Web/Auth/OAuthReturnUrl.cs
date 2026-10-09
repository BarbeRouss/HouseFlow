using System.Web;

namespace HouseFlow.Web.Auth;

/// <summary>
/// OAuth 2.1 authorization server (#304). <c>GET /connect/authorize</c> sends the browser to the
/// front's <c>/oauth/authorize</c> (no <c>oauthSession</c> cookie yet) or <c>/oauth/consent</c>
/// pages with <c>?returnUrl=</c> = the authorization request to resume, and both pages end with a
/// full-page navigation to it. The returnUrl is therefore an open redirect unless it is pinned to
/// the API's own authorization endpoint: <see cref="Validate"/> is the only way to get a URL those
/// pages may navigate to. Plain C# (no Blazor), linked into the unit tests.
/// </summary>
public static class OAuthReturnUrl
{
    /// <summary>The API's authorization endpoint: the only page a returnUrl may lead to.</summary>
    public const string AuthorizePath = "/connect/authorize";

    /// <summary>Added by « Refuser »: the API then answers the client <c>error=access_denied</c>.</summary>
    public const string DeniedParameter = "houseflow_consent=denied";

    /// <summary>
    /// The returnUrl itself when it is the API's authorization endpoint — absolute http(s) URL, same
    /// origin (scheme, host, port) as <paramref name="apiBaseUrl"/>, path exactly
    /// <c>/connect/authorize</c>, no fragment; null otherwise (the page shows an error and navigates
    /// nowhere). Checked on the raw text as well as on the parsed URI, so that no userinfo,
    /// backslash, whitespace, percent-encoded host or dot segment can make the browser read another
    /// URL than the one checked here.
    /// </summary>
    public static string? Validate(string? returnUrl, string? apiBaseUrl)
    {
        if (string.IsNullOrEmpty(returnUrl) || string.IsNullOrWhiteSpace(apiBaseUrl)) return null;
        if (!Uri.TryCreate(apiBaseUrl.Trim(), UriKind.Absolute, out var api) || !IsHttp(api)) return null;

        // A URL built by the API is percent-encoded: none of these can appear in it, and URL
        // parsers disagree on them (a browser reads « \ » as « / » and drops tabs and newlines).
        foreach (var c in returnUrl)
        {
            if (c <= ' ' || char.IsControl(c) || char.IsWhiteSpace(c) || c is '\\' or '#') return null;
        }

        var schemeEnd = returnUrl.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0) return null;
        var scheme = returnUrl[..schemeEnd];
        if (!scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return null;

        // host[:port] only: no userinfo (« user@host »), no percent-encoded or non-ASCII host.
        var authorityStart = schemeEnd + 3;
        var pathStart = returnUrl.IndexOfAny(['/', '?'], authorityStart);
        if (pathStart <= authorityStart) return null;
        foreach (var c in returnUrl.AsSpan(authorityStart, pathStart - authorityStart))
        {
            if (c is '@' or '%' || c > '~') return null;
        }

        var queryStart = returnUrl.IndexOf('?', pathStart);
        var path = queryStart < 0 ? returnUrl[pathStart..] : returnUrl[pathStart..queryStart];
        if (!string.Equals(path, AuthorizePath, StringComparison.Ordinal)) return null;

        if (!Uri.TryCreate(returnUrl, UriKind.Absolute, out var url) || !IsHttp(url)) return null;
        var sameOrigin = string.Equals(url.Scheme, api.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(url.Host, api.Host, StringComparison.OrdinalIgnoreCase)
            && url.Port == api.Port
            && url.UserInfo.Length == 0;
        return sameOrigin ? returnUrl : null;
    }

    /// <summary>
    /// Single value of a query parameter of a validated returnUrl, decoded as the API reads it
    /// (<c>+</c> = space, names case-sensitive). Null when absent, empty or repeated (the API
    /// refuses a request with a repeated parameter).
    /// </summary>
    public static string? Parameter(string returnUrl, string name)
    {
        var queryStart = returnUrl.IndexOf('?');
        if (queryStart < 0) return null;

        string? found = null;
        foreach (var pair in returnUrl[(queryStart + 1)..].Split('&'))
        {
            var eq = pair.IndexOf('=');
            var key = HttpUtility.UrlDecode(eq < 0 ? pair : pair[..eq]);
            if (!string.Equals(key, name, StringComparison.Ordinal)) continue;
            if (found is not null) return null;
            found = eq < 0 ? "" : HttpUtility.UrlDecode(pair[(eq + 1)..]);
        }
        return string.IsNullOrEmpty(found) ? null : found;
    }

    /// <summary>« Refuser »: the validated returnUrl with <see cref="DeniedParameter"/> appended.</summary>
    public static string Denied(string returnUrl) =>
        returnUrl + (returnUrl.Contains('?') ? "&" : "?") + DeniedParameter;

    /// <summary>host[:port] of a validated returnUrl (the API), shown while connecting.</summary>
    public static string Host(string returnUrl) => new Uri(returnUrl).Authority;

    private static bool IsHttp(Uri uri) => uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
}

/// <summary>Scopes of the authorization server (#304), as the consent screen and P11 show them.</summary>
public static class OAuthScopes
{
    public const string HousesRead = "houses:read";
    public const string HousesWrite = "houses:write";

    /// <summary>OpenIddict's refresh-token scope: always issued with the code flow, never shown to the user.</summary>
    public const string OfflineAccess = "offline_access";

    /// <summary>The scopes the user can grant, in display order.</summary>
    private static readonly string[] Grantable = [HousesRead, HousesWrite];

    /// <summary>
    /// Scopes the consent screen lists (ticked by default, and granted as ticked): requested — the
    /// space-separated <c>scope</c> parameter — ∩ allowed for the client ∩ the grantable scopes, in
    /// display order. Never <c>offline_access</c>, never a scope the screen cannot name.
    /// </summary>
    public static IReadOnlyList<string> Displayed(string? requestedScope, IEnumerable<string>? clientScopes)
    {
        if (string.IsNullOrEmpty(requestedScope) || clientScopes is null) return [];
        var requested = requestedScope.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var allowed = clientScopes.ToHashSet(StringComparer.Ordinal);
        return Grantable.Where(s => requested.Contains(s) && allowed.Contains(s)).ToList();
    }

    /// <summary>Catalogue key of a grantable scope (<c>housesRead</c>, <c>housesWrite</c>).</summary>
    public static string Key(string scope) => scope switch
    {
        HousesRead => "housesRead",
        HousesWrite => "housesWrite",
        _ => scope,
    };

    /// <summary>
    /// What a connected app may do, as a catalogue key (P11 « Applications connectées »):
    /// <c>readWrite</c>, <c>read</c> or <c>write</c>; null when none of the grantable scopes is granted.
    /// </summary>
    public static string? Summary(IEnumerable<string>? scopes)
    {
        var granted = scopes?.ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal);
        return (granted.Contains(HousesRead), granted.Contains(HousesWrite)) switch
        {
            (true, true) => "readWrite",
            (true, false) => "read",
            (false, true) => "write",
            _ => null,
        };
    }
}
