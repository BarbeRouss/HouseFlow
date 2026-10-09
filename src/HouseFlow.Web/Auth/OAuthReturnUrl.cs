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

    /// <summary>
    /// The refusal marker. The API reads any occurrence of it as a refusal, whatever the case of its
    /// name, its value or how many times it appears.
    /// </summary>
    public const string ConsentParameter = "houseflow_consent";

    /// <summary>Added by « Refuser »: the API then answers the client <c>error=access_denied</c>.</summary>
    public const string DeniedParameter = ConsentParameter + "=denied";

    /// <summary>
    /// Loopback hosts a native client listens on (RFC 8252 §7.3), as the API's RedirectUriPolicy
    /// lists them: plain http is allowed there, and any port.
    /// </summary>
    private static readonly string[] LoopbackHosts = ["127.0.0.1", "[::1]", "localhost"];

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

    /// <summary>
    /// « Refuser »: the validated returnUrl with <see cref="DeniedParameter"/> as its only
    /// <see cref="ConsentParameter"/> — any occurrence already there (name in any case, encoded or
    /// not, any value) is dropped first, so that the API reads exactly one refusal.
    /// </summary>
    public static string Denied(string returnUrl)
    {
        var queryStart = returnUrl.IndexOf('?');
        if (queryStart < 0) return returnUrl + "?" + DeniedParameter;

        var kept = returnUrl[(queryStart + 1)..]
            .Split('&')
            .Where(pair => pair.Length > 0 && !IsConsentParameter(pair))
            .Append(DeniedParameter);
        return returnUrl[..(queryStart + 1)] + string.Join('&', kept);
    }

    /// <summary>host[:port] of a validated returnUrl (the API), shown while connecting.</summary>
    public static string Host(string returnUrl) => new Uri(returnUrl).Authority;

    /// <summary>
    /// Where the request sends the user back, as the consent screen names it: host[:port] of its
    /// <c>redirect_uri</c> in the format of the API's <c>redirectHosts</c> — ASCII host (punycode for
    /// a non-ASCII name: a look-alike shows as <c>xn--…</c>), IPv6 in brackets, port only when it is
    /// not the scheme's default. Null when the redirect_uri is not one the API registers: absolute
    /// https on a remote host or http on a loopback one, no user info, white space or fragment.
    /// </summary>
    public static string? RedirectHost(string? redirectUri)
    {
        if (string.IsNullOrEmpty(redirectUri)) return null;
        foreach (var c in redirectUri)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c) || c == '#') return null;
        }
        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri) || !IsHttp(uri)) return null;

        // The authority must be spelled out (Uri also reads « https:host/path »), host only.
        if (!redirectUri.StartsWith(uri.Scheme + "://", StringComparison.OrdinalIgnoreCase)
            || uri.UserInfo.Length > 0 || uri.Host.Length == 0) return null;

        // Same rule as the API's registration: http on a loopback host, https anywhere else.
        var loopback = LoopbackHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
        if (loopback != (uri.Scheme == Uri.UriSchemeHttp)) return null;

        string host;
        try
        {
            // IdnHost drops the brackets of an IPv6 address (« ::1:8080 » would be ambiguous): Host keeps them.
            host = uri.HostNameType == UriHostNameType.IPv6 ? uri.Host : uri.IdnHost;
        }
        catch (UriFormatException)
        {
            return null; // a name IDNA cannot convert (U+FFFD, joiners…)
        }
        return uri.IsDefaultPort ? host : $"{host}:{uri.Port}";
    }

    /// <summary>
    /// True when <paramref name="redirectHost"/> (<see cref="RedirectHost"/> of the request) is one
    /// the client registered — its <c>redirectHosts</c> — so that the consent screen never names a
    /// host that only the URL claims. A loopback host on any port also matches a loopback redirect
    /// URI registered without a port: the API accepts any port there for a native client (RFC 8252 §7.3).
    /// </summary>
    public static bool IsRegisteredHost(string redirectHost, IEnumerable<string>? registeredHosts)
    {
        if (registeredHosts is null) return false;
        var name = HostWithoutPort(redirectHost);
        var anyPort = LoopbackHosts.Contains(name, StringComparer.OrdinalIgnoreCase);
        return registeredHosts.Any(registered =>
            string.Equals(registered, redirectHost, StringComparison.OrdinalIgnoreCase)
            || (anyPort && string.Equals(registered, name, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>« [::1] » for « [::1]:8080 », « 127.0.0.1 » for « 127.0.0.1:9 ».</summary>
    private static string HostWithoutPort(string host)
    {
        var portStart = host.StartsWith('[') ? host.IndexOf("]:", StringComparison.Ordinal) + 1 : host.IndexOf(':');
        return portStart > 0 ? host[..portStart] : host;
    }

    /// <summary>A <c>name[=value]</c> pair of the query whose decoded name is <see cref="ConsentParameter"/>, in any case.</summary>
    private static bool IsConsentParameter(string pair)
    {
        var eq = pair.IndexOf('=');
        return string.Equals(HttpUtility.UrlDecode(eq < 0 ? pair : pair[..eq]), ConsentParameter, StringComparison.OrdinalIgnoreCase);
    }

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
