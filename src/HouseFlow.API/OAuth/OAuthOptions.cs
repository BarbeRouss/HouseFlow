using System.ComponentModel.DataAnnotations;

namespace HouseFlow.API.OAuth;

/// <summary>
/// Configuration section <c>OAuth</c> of the embedded OAuth 2.1 authorization server (issue #304).
/// Validated at startup: an access token lifetime above 15 minutes, for instance, refuses to boot.
/// </summary>
public sealed class OAuthOptions
{
    public const string SectionName = "OAuth";

    /// <summary>Front end used when neither <see cref="WebBaseUrl"/> nor <c>CORS__ORIGINS</c> is configured.</summary>
    public const string DefaultWebBaseUrl = "http://localhost:3000";

    /// <summary>
    /// Origin of the HouseFlow front end, where <c>/connect/authorize</c> sends the user to log in
    /// (<c>/oauth/authorize</c>) and to consent (<c>/oauth/consent</c>). Defaults to the first origin
    /// of <c>CORS__ORIGINS</c> — configured in every environment — then <see cref="DefaultWebBaseUrl"/>.
    /// Resolved at startup: never null once the options are built.
    /// </summary>
    public string? WebBaseUrl { get; set; }

    /// <summary>Issuer of the tokens; by default OpenIddict derives it from the request (scheme + host).</summary>
    public string? Issuer { get; set; }

    /// <summary>
    /// Resource servers (RFC 8707) tokens can be issued for, i.e. their audience: the URL of the MCP
    /// server (issue #305). Empty: <c>{scheme}://{host}/mcp</c> of the request.
    /// </summary>
    public string[] Resources { get; set; } = [];

    /// <summary>OAuth 2.1 / issue #304: access tokens stay short-lived (15 minutes at most).</summary>
    [Range(1, 15)]
    public int AccessTokenLifetimeMinutes { get; set; } = 15;

    /// <summary>Sliding: every refresh (rotation) issues a refresh token valid for this long again.</summary>
    [Range(1, 365)]
    public int RefreshTokenLifetimeDays { get; set; } = 30;

    /// <summary>RFC 6749 §4.1.2 recommends 10 minutes at most.</summary>
    [Range(1, 10)]
    public int AuthorizationCodeLifetimeMinutes { get; set; } = 5;

    /// <summary>
    /// Lifetime of the token in the <c>oauthSession</c> cookie (the user's identity on
    /// <c>/connect/authorize</c>) — a session cookie, which this expiry bounds.
    /// </summary>
    [Range(1, 60)]
    public int SessionCookieLifetimeMinutes { get; set; } = 10;

    /// <summary>The configured URIs are absolute http(s) URIs (resources: without a fragment).</summary>
    public bool HasValidUris() =>
        IsHttpUri(WebBaseUrl) && (Issuer is null || IsHttpUri(Issuer)) &&
        Resources.All(resource => IsHttpUri(resource) && !resource.Contains('#'));

    /// <summary><see cref="WebBaseUrl"/>, else the first origin of <c>CORS__ORIGINS</c>, else <see cref="DefaultWebBaseUrl"/>.</summary>
    public static string ResolveWebBaseUrl(string? configured, string? corsOrigins)
    {
        var url = !string.IsNullOrWhiteSpace(configured)
            ? configured
            : corsOrigins?
                  .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .FirstOrDefault(origin => origin != "*")
              ?? DefaultWebBaseUrl;

        return url.Trim().TrimEnd('/');
    }

    private static bool IsHttpUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}
