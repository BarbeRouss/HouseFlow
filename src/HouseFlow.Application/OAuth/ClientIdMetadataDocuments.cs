using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using System.Text.Json.Serialization;

namespace HouseFlow.Application.OAuth;

/// <summary>
/// Client ID Metadata Documents (CIMD, MCP authorization spec 2025-11, issue #308): the
/// <c>client_id</c> is an https URL, and the document served there describes the client. Which
/// <c>client_id</c> may be fetched at all is decided here — it is what an anonymous caller hands the
/// authorization server to download.
/// </summary>
public static class ClientIdMetadataUrl
{
    /// <summary>Size of the <c>OpenIddictApplications.ClientId</c> column (migration of #309).</summary>
    public const int MaxLength = 100;

    /// <summary>A <c>client_id</c> that claims to be a URL (has a scheme): it must then be a valid one.</summary>
    public static bool LooksLikeUrl(string? clientId) =>
        !string.IsNullOrEmpty(clientId) && clientId.Contains("://", StringComparison.Ordinal);

    /// <summary>
    /// https on the default port, a DNS name (never an IP literal), a non-empty path, no user info,
    /// fragment, dot segment or white space, ASCII only (an internationalized host in its xn-- form,
    /// as everywhere else a host is shown to the user) and at most <see cref="MaxLength"/> characters.
    /// A query is allowed (the draft allows it).
    /// </summary>
    public static bool TryParse(string? value, [NotNullWhen(true)] out Uri? url)
    {
        url = null;
        if (string.IsNullOrEmpty(value) || value.Length > MaxLength) return false;
        if (!Ascii.IsValid(value) || value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)) || value.Contains('#'))
            return false;
        if (!value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return false;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;

        if (uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 || !uri.IsDefaultPort) return false;
        if (!string.IsNullOrEmpty(uri.UserInfo) || uri.HostNameType != UriHostNameType.Dns) return false;
        if (uri.Host.TrimEnd('.').Length == 0 || uri.AbsolutePath == "/") return false;
        // Compared on the raw text: Uri would have resolved a ".." — and the document names this exact string.
        var path = value.AsSpan("https://".Length);
        path = path[path.IndexOf('/')..];
        if (path.IndexOf('?') is var query and >= 0) path = path[..query];
        if (path.ToString().Split('/').Any(segment => segment is "." or ".." or "%2e" or "%2E" or "%2e%2e" or "%2E%2E"))
            return false;

        url = uri;
        return true;
    }

    /// <summary>The host of a client id URL as the consent screen shows it (<see cref="RedirectUriPolicy.DisplayHost"/>).</summary>
    public static string Origin(Uri clientIdUrl) => RedirectUriPolicy.DisplayHost(clientIdUrl);
}

/// <summary>
/// A Client ID Metadata Document: the RFC 7591 client metadata HouseFlow acts upon, plus the
/// <c>client_id</c> that must name the URL it was served at.
/// </summary>
public sealed class ClientIdMetadataDocument
{
    [JsonPropertyName("client_id")]
    public string? ClientId { get; init; }

    [JsonPropertyName("client_name")]
    public string? ClientName { get; init; }

    [JsonPropertyName("redirect_uris")]
    public List<string?>? RedirectUris { get; init; }

    [JsonPropertyName("token_endpoint_auth_method")]
    public string? TokenEndpointAuthMethod { get; init; }

    [JsonPropertyName("grant_types")]
    public List<string?>? GrantTypes { get; init; }

    [JsonPropertyName("response_types")]
    public List<string?>? ResponseTypes { get; init; }

    [JsonPropertyName("scope")]
    public string? Scope { get; init; }

    [JsonPropertyName("client_uri")]
    public string? ClientUri { get; init; }

    public ClientRegistrationRequest ToRegistrationRequest() => new()
    {
        ClientName = ClientName,
        RedirectUris = RedirectUris,
        TokenEndpointAuthMethod = TokenEndpointAuthMethod,
        GrantTypes = GrantTypes,
        ResponseTypes = ResponseTypes,
        Scope = Scope,
        ClientUri = ClientUri
    };
}

/// <summary>
/// What HouseFlow accepts from a Client ID Metadata Document: what <see cref="ClientRegistrationValidator"/>
/// accepts from a registration, and, to make the <c>client_id</c> an identity worth showing to the
/// user, <c>client_id</c> equal to the URL the document was fetched at and every https redirect URI
/// on <b>exactly the host of that URL</b> (default port; no public suffix list: <c>app.x.com</c> and
/// <c>auth.x.com</c> are two hosts — such a client keeps the DCR). A loopback redirect URI stays
/// admitted (native clients), but proves nothing: any local program can present a public <c>client_id</c>.
/// </summary>
public static class ClientMetadataDocumentValidator
{
    public static bool TryValidate(
        Uri clientIdUrl,
        ClientIdMetadataDocument? document,
        IReadOnlyCollection<Uri> reservedOrigins,
        [NotNullWhen(true)] out ValidatedClientRegistration? registration,
        [NotNullWhen(false)] out string? error)
    {
        registration = null;
        if (document is null)
        {
            error = "The client metadata document must be a JSON object.";
            return false;
        }

        if (!string.Equals(document.ClientId, clientIdUrl.OriginalString, StringComparison.Ordinal))
        {
            error = "The client_id of the document must be the URL it was fetched at.";
            return false;
        }

        if (!ClientRegistrationValidator.TryValidate(
                document.ToRegistrationRequest(), reservedOrigins, out var validated, out var invalid))
        {
            error = invalid.ErrorDescription;
            return false;
        }

        var host = HostName(clientIdUrl);
        foreach (var uri in validated.RedirectUris.Select(uri => new Uri(uri, UriKind.Absolute)))
        {
            if (RedirectUriPolicy.IsLoopback(uri)) continue;
            if (!uri.IsDefaultPort || !string.Equals(HostName(uri), host, StringComparison.OrdinalIgnoreCase))
            {
                error = "Every https redirect URI must be on the host of the client_id (default port).";
                return false;
            }
        }

        registration = validated;
        error = null;
        return true;
    }

    private static string HostName(Uri uri) => RedirectUriPolicy.DisplayHost(uri).TrimEnd('.');
}

/// <summary>How long a downloaded document is trusted (the cache is the application row itself).</summary>
public static class ClientMetadataCachePolicy
{
    public static readonly TimeSpan MinTtl = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MaxTtl = TimeSpan.FromHours(1);

    /// <summary>How long a failed download is remembered, so that HouseFlow is not an amplifier against a third party.</summary>
    public static readonly TimeSpan NegativeTtl = TimeSpan.FromMinutes(1);

    /// <summary><c>Cache-Control: max-age</c> bounded to [5 min, 1 h]; 1 h when the server states none.</summary>
    public static TimeSpan Ttl(TimeSpan? maxAge) =>
        maxAge is null ? MaxTtl : maxAge.Value < MinTtl ? MinTtl : maxAge.Value > MaxTtl ? MaxTtl : maxAge.Value;
}

public enum ClientMetadataFetchFailure
{
    None,
    RateLimited,
    Unreachable,
    NonPublicAddress,
    BadStatus,
    BadContentType,
    TooLarge,
    InvalidJson
}

/// <summary>The outcome of a download: a document, or the reason it could not be had.</summary>
public sealed record ClientMetadataFetchResult(
    ClientIdMetadataDocument? Document, TimeSpan? MaxAge, ClientMetadataFetchFailure Failure)
{
    public bool Succeeded => Document is not null && Failure == ClientMetadataFetchFailure.None;

    public static ClientMetadataFetchResult Success(ClientIdMetadataDocument document, TimeSpan? maxAge) =>
        new(document, maxAge, ClientMetadataFetchFailure.None);

    public static ClientMetadataFetchResult Failed(ClientMetadataFetchFailure failure) => new(null, null, failure);
}

/// <summary>
/// Downloads a client metadata document. The implementation is the SSRF boundary: it must connect
/// to public addresses only, follow no redirect, bound size and time, and rate-limit its callers.
/// </summary>
public interface IClientMetadataDocumentFetcher
{
    /// <param name="url">A URL accepted by <see cref="ClientIdMetadataUrl.TryParse"/>.</param>
    /// <param name="callerKey">The rate-limit bucket of the caller (its IP, see the API's HttpEdge).</param>
    Task<ClientMetadataFetchResult> FetchAsync(Uri url, string callerKey, CancellationToken cancellationToken = default);
}

/// <summary>Whether an address a document may be fetched from is a public one (SSRF guard).</summary>
public static class OutboundAddressPolicy
{
    /// <summary>The Azure wireserver / IMDS helper: in a public range, yet reachable from any VM.</summary>
    private static readonly IPAddress AzureWireServer = IPAddress.Parse("168.63.129.16");

    private static readonly (string Network, int Prefix)[] BlockedV4 =
    [
        ("0.0.0.0", 8), ("10.0.0.0", 8), ("100.64.0.0", 10), ("127.0.0.0", 8), ("169.254.0.0", 16),
        ("172.16.0.0", 12), ("192.0.0.0", 24), ("192.0.2.0", 24), ("192.88.99.0", 24), ("192.168.0.0", 16),
        ("198.18.0.0", 15), ("198.51.100.0", 24), ("203.0.113.0", 24), ("224.0.0.0", 4), ("240.0.0.0", 4)
    ];

    // IPv6 is allowed inside the global unicast range 2000::/3 only, minus the blocks below —
    // transition mechanisms and documentation prefixes that can wrap or stand for an IPv4 address.
    private static readonly (string Network, int Prefix)[] BlockedV6 =
    [
        ("2001::", 32), ("2001:db8::", 32), ("2002::", 16), ("3fff::", 20)
    ];

    public static bool IsPublic(IPAddress address)
    {
        // ::ffff:10.0.0.1 is 10.0.0.1: judged as the IPv4 address it wraps.
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            if (address.Equals(AzureWireServer)) return false;
            return !BlockedV4.Any(range => InRange(address, range.Network, range.Prefix));
        }

        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6) return false;
        if (!InRange(address, "2000::", 3)) return false;
        return !BlockedV6.Any(range => InRange(address, range.Network, range.Prefix));
    }

    private static bool InRange(IPAddress address, string network, int prefix)
    {
        var bytes = address.GetAddressBytes();
        var net = IPAddress.Parse(network).GetAddressBytes();
        var fullBytes = prefix / 8;
        for (var i = 0; i < fullBytes; i++)
            if (bytes[i] != net[i]) return false;

        var remaining = prefix % 8;
        if (remaining == 0) return true;
        var mask = (byte)(0xFF << (8 - remaining));
        return (bytes[fullBytes] & mask) == (net[fullBytes] & mask);
    }
}
