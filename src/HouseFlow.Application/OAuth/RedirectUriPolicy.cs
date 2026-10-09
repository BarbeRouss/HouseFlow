namespace HouseFlow.Application.OAuth;

/// <summary>
/// Which redirect URIs a dynamically registered OAuth client may declare (RFC 7591), and so where
/// an authorization code can ever be sent. Registration is anonymous: this rule is what keeps a
/// code from travelling in clear text to an arbitrary host.
/// </summary>
public static class RedirectUriPolicy
{
    /// <summary>Upper bound on a redirect URI, far above any real one, so that a registration stays small.</summary>
    public const int MaxLength = 2000;

    /// <summary>
    /// Loopback hosts a native client listens on (RFC 8252 §7.3). Plain HTTP is acceptable there:
    /// the redirection never leaves the user's machine.
    /// </summary>
    private static readonly string[] LoopbackHosts = ["127.0.0.1", "[::1]", "localhost"];

    /// <summary>
    /// <c>https://</c> on a non-loopback host, or <c>http://</c> on a loopback host
    /// (<c>127.0.0.1</c>, <c>[::1]</c>, <c>localhost</c>), any port. Never a fragment (RFC 6749
    /// §3.1.2), user info, white space, another scheme (custom schemes can be claimed by any app on
    /// the device) or plain HTTP to a remote host.
    /// </summary>
    public static bool IsAllowed(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxLength) return false;
        if (value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)) || value.Contains('#')) return false;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;

        // The authority must be spelled out: Uri also accepts forms like "https:host/path".
        if (!value.StartsWith(uri.Scheme + "://", StringComparison.OrdinalIgnoreCase)) return false;
        if (string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo)) return false;

        return uri.Scheme switch
        {
            "https" => !IsLoopback(uri),
            "http" => IsLoopback(uri),
            _ => false
        };
    }

    /// <summary>True for a URI on one of the loopback hosts a native client listens on.</summary>
    public static bool IsLoopback(Uri uri) =>
        LoopbackHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Where a redirect URI sends the user, as the consent screen and the connected applications
    /// show it: the host in its ASCII form (punycode for an internationalized name, so that no
    /// look-alike letter passes for another), with the port unless it is the scheme's default —
    /// <c>claude.ai</c>, <c>127.0.0.1:9</c>, <c>[::1]:8080</c>.
    /// </summary>
    public static string DisplayHost(Uri uri)
    {
        // IdnHost drops the brackets of an IPv6 address, which a port needs.
        var host = uri.HostNameType == UriHostNameType.IPv6 ? uri.Host : uri.IdnHost;
        return uri.IsDefaultPort ? host : $"{host}:{uri.Port}";
    }
}
