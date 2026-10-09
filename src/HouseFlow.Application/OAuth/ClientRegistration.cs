using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace HouseFlow.Application.OAuth;

/// <summary>
/// Client metadata of a Dynamic Client Registration request (RFC 7591 §2). Only the fields HouseFlow
/// acts upon are bound; the others are ignored, as RFC 7591 §2 requires.
/// </summary>
public sealed class ClientRegistrationRequest
{
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
}

/// <summary>Client information response of a successful registration (RFC 7591 §3.2.1). Never a secret.</summary>
public sealed record ClientRegistrationResponse(
    [property: JsonPropertyName("client_id")] string ClientId,
    [property: JsonPropertyName("client_id_issued_at")] long ClientIdIssuedAt,
    [property: JsonPropertyName("client_name")] string ClientName,
    [property: JsonPropertyName("redirect_uris")] IReadOnlyList<string> RedirectUris,
    [property: JsonPropertyName("grant_types")] IReadOnlyList<string> GrantTypes,
    [property: JsonPropertyName("response_types")] IReadOnlyList<string> ResponseTypes,
    [property: JsonPropertyName("token_endpoint_auth_method")] string TokenEndpointAuthMethod,
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("client_uri"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ClientUri);

/// <summary>Error response of a refused registration (RFC 7591 §3.2.2).</summary>
public sealed record ClientRegistrationError(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("error_description")] string ErrorDescription)
{
    public const string InvalidRedirectUri = "invalid_redirect_uri";
    public const string InvalidClientMetadata = "invalid_client_metadata";
}

/// <summary>A registration request that passed <see cref="ClientRegistrationValidator"/>, defaults applied.</summary>
public sealed record ValidatedClientRegistration(
    string ClientName,
    IReadOnlyList<string> RedirectUris,
    IReadOnlyList<string> GrantTypes,
    IReadOnlyList<string> ResponseTypes,
    IReadOnlyList<string> Scopes,
    string? ClientUri)
{
    /// <summary>Whether the client may redeem refresh tokens (and so be issued some).</summary>
    public bool AllowsRefreshTokens => GrantTypes.Contains(ClientRegistrationValidator.RefreshTokenGrant);
}

/// <summary>
/// What HouseFlow accepts from an anonymous Dynamic Client Registration request (RFC 7591): public
/// clients only (no secret, PKCE enforced by the server), the authorization code flow and its
/// refresh tokens, the HouseFlow scopes, and redirect URIs allowed by <see cref="RedirectUriPolicy"/>
/// that do not point to HouseFlow itself.
/// </summary>
public static class ClientRegistrationValidator
{
    public const string AuthorizationCodeGrant = "authorization_code";
    public const string RefreshTokenGrant = "refresh_token";
    public const string CodeResponseType = "code";
    public const string NoClientAuthentication = "none";

    public const int MaxClientNameLength = 100;
    public const int MaxRedirectUris = 5;

    private static readonly string[] SupportedGrantTypes = [AuthorizationCodeGrant, RefreshTokenGrant];

    /// <param name="reservedOrigins">
    /// HouseFlow's own origins (the API, the front end): no redirect URI may point there — a client
    /// would pass for HouseFlow on the consent screen (« renvoyé vers houseflow… »), and a code sent
    /// there could leak through any open redirect. A remote host is reserved on every port; a
    /// loopback one (development) on its port only, on any loopback name.
    /// </param>
    public static bool TryValidate(
        ClientRegistrationRequest? request,
        IReadOnlyCollection<Uri> reservedOrigins,
        [NotNullWhen(true)] out ValidatedClientRegistration? registration,
        [NotNullWhen(false)] out ClientRegistrationError? error)
    {
        error = Check(request, reservedOrigins, out var validated);
        if (error is not null || validated is null)
        {
            registration = null;
            error ??= Metadata("Invalid client metadata.");
            return false;
        }

        registration = validated;
        return true;
    }

    private static ClientRegistrationError? Check(
        ClientRegistrationRequest? request, IReadOnlyCollection<Uri> reservedOrigins, out ValidatedClientRegistration? registration)
    {
        registration = null;
        if (request is null)
            return Metadata("The request body must be a JSON object of client metadata.");

        // ---- client_name: shown on the consent screen, so required and plain.
        var clientName = request.ClientName?.Trim();
        if (string.IsNullOrEmpty(clientName))
            return Metadata("client_name is required.");
        if (clientName.Length > MaxClientNameLength)
            return Metadata($"client_name must not exceed {MaxClientNameLength} characters.");
        if (clientName.Any(IsUnsafeDisplayCharacter))
            return Metadata("client_name must not contain control or bidirectional formatting characters.");

        // ---- redirect_uris: where codes are sent.
        if (request.RedirectUris is not { Count: > 0 })
            return RedirectUri("redirect_uris is required and must contain at least one URI.");
        if (request.RedirectUris.Count > MaxRedirectUris)
            return RedirectUri($"At most {MaxRedirectUris} redirect URIs can be registered.");
        if (!request.RedirectUris.All(RedirectUriPolicy.IsAllowed))
            return RedirectUri("Redirect URIs must use https, or http on a loopback host (127.0.0.1, [::1], localhost), "
                + "be ASCII (an internationalized host in its xn-- form) and have no fragment.");
        var redirectUris = request.RedirectUris.OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        if (redirectUris.Any(uri => IsReserved(new Uri(uri, UriKind.Absolute), reservedOrigins)))
            return RedirectUri("Redirect URIs must not point to HouseFlow itself.");

        // ---- token_endpoint_auth_method: public clients only.
        if (request.TokenEndpointAuthMethod is not (null or NoClientAuthentication))
            return Metadata("Only public clients are supported: token_endpoint_auth_method must be \"none\".");

        // ---- grant_types / response_types: the authorization code flow, with or without refresh tokens.
        var requestedGrantTypes = request.GrantTypes ?? [.. SupportedGrantTypes];
        if (requestedGrantTypes.Any(grant => !SupportedGrantTypes.Contains(grant, StringComparer.Ordinal)))
            return Metadata("Only the authorization_code and refresh_token grant types are supported.");
        var grantTypes = requestedGrantTypes.OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        if (!grantTypes.Contains(AuthorizationCodeGrant, StringComparer.Ordinal))
            return Metadata("The authorization_code grant type is required.");

        var responseTypes = (request.ResponseTypes ?? [CodeResponseType]).Distinct(StringComparer.Ordinal).ToList();
        if (responseTypes is not [CodeResponseType])
            return Metadata("Only the \"code\" response type is supported.");

        // ---- scope: the HouseFlow scopes (all of them when omitted); offline_access is tolerated
        // and ignored — whether the client gets refresh tokens follows its grant types.
        var requestedScopes = (request.Scope ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (requestedScopes.Any(scope => !OAuthScopes.IsGrantable(scope) && scope != OAuthScopes.OfflineAccess))
            return Metadata($"Unsupported scope. Supported scopes: {string.Join(' ', OAuthScopes.Grantable)}.");
        var scopes = requestedScopes.Count == 0
            ? OAuthScopes.Grantable.ToList()
            : OAuthScopes.Grantable.Where(requestedScopes.Contains).ToList();
        if (scopes.Count == 0)
            return Metadata($"At least one of these scopes is required: {string.Join(' ', OAuthScopes.Grantable)}.");

        // ---- client_uri: informative home page of the client, https only.
        var clientUri = string.IsNullOrWhiteSpace(request.ClientUri) ? null : request.ClientUri.Trim();
        if (clientUri is not null && !IsHttpsPage(clientUri))
            return Metadata("client_uri must be an absolute https URI without a fragment.");

        registration = new ValidatedClientRegistration(
            clientName, redirectUris, grantTypes, [CodeResponseType], scopes, clientUri);
        return null;
    }

    private static bool IsReserved(Uri redirectUri, IEnumerable<Uri> reservedOrigins) =>
        reservedOrigins.Any(origin => RedirectUriPolicy.IsLoopback(origin)
            ? RedirectUriPolicy.IsLoopback(redirectUri) && redirectUri.Port == origin.Port
            : string.Equals(HostName(redirectUri), HostName(origin), StringComparison.OrdinalIgnoreCase));

    /// <summary>The host without the final dot of a fully qualified name (<c>houseflow.app.</c> is <c>houseflow.app</c>).</summary>
    private static string HostName(Uri uri) => uri.Host.TrimEnd('.');

    private static bool IsHttpsPage(string value) =>
        value.Length <= RedirectUriPolicy.MaxLength
        && !value.Contains('#')
        && !value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))
        && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrEmpty(uri.Host)
        && string.IsNullOrEmpty(uri.UserInfo);

    /// <summary>
    /// Characters that would let a client name lie about itself on the consent screen: control
    /// characters and the Unicode bidirectional overrides/isolates (« Trojan Source » style).
    /// </summary>
    private static bool IsUnsafeDisplayCharacter(char c) =>
        char.IsControl(c)
        || c is (>= '\u202A' and <= '\u202E') or (>= '\u2066' and <= '\u2069') or '\u200E' or '\u200F' or '\u061C';

    private static ClientRegistrationError RedirectUri(string description) =>
        new(ClientRegistrationError.InvalidRedirectUri, description);

    private static ClientRegistrationError Metadata(string description) =>
        new(ClientRegistrationError.InvalidClientMetadata, description);
}
