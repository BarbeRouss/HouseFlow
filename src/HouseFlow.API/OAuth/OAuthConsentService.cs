using System.Text.Json;
using HouseFlow.Application.DTOs;
using HouseFlow.Application.OAuth;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace HouseFlow.API.OAuth;

/// <summary>
/// OAuth clients (registered by DCR) and the users' consents to them — permanent OpenIddict
/// authorizations, one valid per (user, client): a new consent replaces the scopes of the existing
/// one. Shared by the authorization endpoint and the <c>/api/v1/oauth</c> screens.
/// </summary>
public sealed class OAuthConsentService
{
    /// <summary>Unix time (seconds) of the registration, returned as <c>client_id_issued_at</c> (RFC 7591).</summary>
    public const string ClientIdIssuedAtProperty = "client_id_issued_at";

    /// <summary>Self-declared home page of the client (RFC 7591 <c>client_uri</c>).</summary>
    public const string ClientUriProperty = "client_uri";

    /// <summary>
    /// Property of an authorization: the redirect hosts (<see cref="RedirectUriPolicy.DisplayHost"/>,
    /// JSON array) the user consented to send codes to — those the consent screen showed.
    /// </summary>
    public const string RedirectHostsProperty = "redirect_hosts";

    private readonly IOpenIddictApplicationManager _applications;
    private readonly IOpenIddictAuthorizationManager _authorizations;
    private readonly IOpenIddictTokenManager _tokens;
    private readonly ILogger<OAuthConsentService> _logger;

    public OAuthConsentService(
        IOpenIddictApplicationManager applications,
        IOpenIddictAuthorizationManager authorizations,
        IOpenIddictTokenManager tokens,
        ILogger<OAuthConsentService> logger)
    {
        _applications = applications;
        _authorizations = authorizations;
        _tokens = tokens;
        _logger = logger;
    }

    public async Task<object?> FindClientAsync(string clientId, CancellationToken cancellationToken = default) =>
        await _applications.FindByClientIdAsync(clientId, cancellationToken);

    /// <summary>The scopes the client was registered for (its <c>scp:</c> permissions), never <c>offline_access</c>.</summary>
    public async Task<IReadOnlyList<string>> GetClientScopesAsync(object application, CancellationToken cancellationToken = default)
    {
        var permissions = await _applications.GetPermissionsAsync(application, cancellationToken);
        return OAuthScopes.Grantable
            .Where(scope => permissions.Contains(Permissions.Prefixes.Scope + scope, StringComparer.Ordinal))
            .ToList();
    }

    public async Task<OAuthClientInfoDto> DescribeClientAsync(object application, CancellationToken cancellationToken = default)
    {
        var clientId = await _applications.GetClientIdAsync(application, cancellationToken) ?? string.Empty;
        var properties = await _applications.GetPropertiesAsync(application, cancellationToken);

        return new OAuthClientInfoDto(
            ClientId: clientId,
            ClientName: await _applications.GetDisplayNameAsync(application, cancellationToken) ?? clientId,
            ClientUri: properties.TryGetValue(ClientUriProperty, out var clientUri) && clientUri.ValueKind == JsonValueKind.String
                ? clientUri.GetString()
                : null,
            RedirectHosts: await GetRedirectHostsAsync(application, cancellationToken),
            Scopes: await GetClientScopesAsync(application, cancellationToken));
    }

    /// <summary>
    /// Whether <paramref name="redirectUri"/> is one of the client's redirect URIs, by OpenIddict's
    /// own rule — the one the authorization endpoint applies (loopback port of a native client).
    /// </summary>
    public async Task<bool> IsRedirectUriOfAsync(object application, string redirectUri, CancellationToken cancellationToken = default) =>
        await _applications.ValidateRedirectUriAsync(application, redirectUri, cancellationToken);

    /// <summary>The hosts of the client's redirect URIs (<see cref="RedirectUriPolicy.DisplayHost"/>): the part the user can judge.</summary>
    private async Task<IReadOnlyList<string>> GetRedirectHostsAsync(object application, CancellationToken cancellationToken) =>
        (await _applications.GetRedirectUrisAsync(application, cancellationToken))
            .Select(uri => Uri.TryCreate(uri, UriKind.Absolute, out var parsed) ? RedirectUriPolicy.DisplayHost(parsed) : null)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// The user's valid permanent authorization for the client (the most recent one, should a race
    /// have created two), and the scopes and redirect hosts the user consented to across them.
    /// </summary>
    public async Task<(string? AuthorizationId, IReadOnlyList<string> GrantedScopes, IReadOnlyList<string> RedirectHosts)>
        FindConsentAsync(Guid userId, object application, CancellationToken cancellationToken = default)
    {
        var authorizations = await FindValidAuthorizationsAsync(userId, application, cancellationToken);
        return authorizations.Count == 0
            ? (null, [], [])
            : (await _authorizations.GetIdAsync(authorizations[0], cancellationToken),
               await GrantedScopesAsync(authorizations, cancellationToken),
               await ConsentedHostsAsync(authorizations, cancellationToken));
    }

    /// <summary>
    /// The user's valid permanent authorizations for the client, most recent first: one, unless a
    /// race between two consents created a second — which the next consent revokes.
    /// </summary>
    private async Task<List<object>> FindValidAuthorizationsAsync(
        Guid userId, object application, CancellationToken cancellationToken)
    {
        var applicationId = await _applications.GetIdAsync(application, cancellationToken);
        var found = new List<(object Authorization, DateTimeOffset? Creation)>();

        await foreach (var authorization in _authorizations.FindAsync(
            subject: userId.ToString(), client: applicationId, status: Statuses.Valid,
            type: AuthorizationTypes.Permanent, scopes: null, cancellationToken))
        {
            found.Add((authorization, await _authorizations.GetCreationDateAsync(authorization, cancellationToken)));
        }

        return [.. found.OrderByDescending(entry => entry.Creation).Select(entry => entry.Authorization)];
    }

    /// <summary>The HouseFlow scopes granted across <paramref name="authorizations"/>, in display order.</summary>
    private async Task<IReadOnlyList<string>> GrantedScopesAsync(
        IEnumerable<object> authorizations, CancellationToken cancellationToken)
    {
        var granted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var authorization in authorizations)
            granted.UnionWith(await _authorizations.GetScopesAsync(authorization, cancellationToken));

        return OAuthScopes.Grantable.Where(granted.Contains).ToList();
    }

    /// <summary>The redirect hosts consented to across <paramref name="authorizations"/>.</summary>
    private async Task<IReadOnlyList<string>> ConsentedHostsAsync(
        IEnumerable<object> authorizations, CancellationToken cancellationToken)
    {
        var hosts = new List<string>();
        foreach (var authorization in authorizations)
            hosts.AddRange(await ConsentedHostsAsync(authorization, cancellationToken));

        return hosts.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The redirect hosts of one authorization: none for a consent given before they were recorded.</summary>
    private async Task<IReadOnlyList<string>> ConsentedHostsAsync(object authorization, CancellationToken cancellationToken)
    {
        var properties = await _authorizations.GetPropertiesAsync(authorization, cancellationToken);
        return properties.TryGetValue(RedirectHostsProperty, out var hosts) && hosts.ValueKind == JsonValueKind.Array
            ? hosts.EnumerateArray().Where(host => host.ValueKind == JsonValueKind.String).Select(host => host.GetString()!).ToList()
            : [];
    }

    /// <summary>
    /// Records the user's consent: the checked scopes replace those of the existing authorization
    /// (or a new one is created), and the host of <paramref name="redirectUri"/> joins the redirect
    /// hosts it covers. A consent that withdraws a scope revokes every token issued under the
    /// authorization: they carry the old scopes, and the client must come back for a code limited
    /// to what is granted now.
    /// </summary>
    public async Task<OAuthAuthorizationDto> GrantAsync(
        Guid userId, object application, IReadOnlyCollection<string> scopes, Uri redirectUri,
        CancellationToken cancellationToken = default)
    {
        var existing = await FindValidAuthorizationsAsync(userId, application, cancellationToken);
        if (existing.Count == 0)
        {
            var created = new OpenIddictAuthorizationDescriptor
            {
                ApplicationId = await _applications.GetIdAsync(application, cancellationToken),
                CreationDate = DateTimeOffset.UtcNow,
                Status = Statuses.Valid,
                Subject = userId.ToString(),
                Type = AuthorizationTypes.Permanent
            };
            created.Scopes.UnionWith(scopes);
            created.Properties[RedirectHostsProperty] =
                JsonSerializer.SerializeToElement(RedirectUriPolicy.WithConsentedHost([], redirectUri));
            return await ToDtoAsync(await _authorizations.CreateAsync(created, cancellationToken), application, cancellationToken);
        }

        var previous = await GrantedScopesAsync(existing, cancellationToken);
        var hosts = RedirectUriPolicy.WithConsentedHost(await ConsentedHostsAsync(existing, cancellationToken), redirectUri);
        var authorization = existing[0];

        var descriptor = new OpenIddictAuthorizationDescriptor();
        await _authorizations.PopulateAsync(descriptor, authorization, cancellationToken);
        descriptor.Scopes.Clear();
        descriptor.Scopes.UnionWith(scopes);
        descriptor.Properties[RedirectHostsProperty] = JsonSerializer.SerializeToElement(hosts);
        await _authorizations.UpdateAsync(authorization, descriptor, cancellationToken);

        // One consent per (user, client): a duplicate left by a race goes, with its tokens.
        foreach (var duplicate in existing.Skip(1))
            await RevokeWithTokensAsync(duplicate, cancellationToken);

        if (previous.Except(scopes, StringComparer.Ordinal).Any())
        {
            var authorizationId = await _authorizations.GetIdAsync(authorization, cancellationToken);
            await _tokens.RevokeByAuthorizationIdAsync(authorizationId!, cancellationToken);
            _logger.LogInformation("OAuth consent {AuthorizationId} narrowed: the tokens issued under it are revoked", authorizationId);
        }

        return await ToDtoAsync(authorization, application, cancellationToken);
    }

    /// <summary>The user's valid consents, most recent first.</summary>
    public async Task<IReadOnlyList<OAuthAuthorizationDto>> ListAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var result = new List<OAuthAuthorizationDto>();

        await foreach (var authorization in _authorizations.FindBySubjectAsync(userId.ToString(), cancellationToken))
        {
            if (!await _authorizations.HasStatusAsync(authorization, Statuses.Valid, cancellationToken) ||
                !await _authorizations.HasTypeAsync(authorization, AuthorizationTypes.Permanent, cancellationToken))
                continue;

            var applicationId = await _authorizations.GetApplicationIdAsync(authorization, cancellationToken);
            var application = applicationId is null ? null : await _applications.FindByIdAsync(applicationId, cancellationToken);
            if (application is null) continue;

            result.Add(await ToDtoAsync(authorization, application, cancellationToken));
        }

        return result.OrderByDescending(authorization => authorization.CreatedAt).ToList();
    }

    /// <summary>
    /// Revokes one of the user's authorizations and every token issued under it (access tokens
    /// included: the validation handler checks token and authorization entries on each use).
    /// False when it does not exist or belongs to someone else.
    /// </summary>
    public async Task<bool> RevokeAsync(Guid userId, string authorizationId, CancellationToken cancellationToken = default)
    {
        var authorization = await _authorizations.FindByIdAsync(authorizationId, cancellationToken);
        if (authorization is null ||
            await _authorizations.GetSubjectAsync(authorization, cancellationToken) != userId.ToString())
            return false;

        await RevokeWithTokensAsync(authorization, cancellationToken);
        return true;
    }

    private async Task RevokeWithTokensAsync(object authorization, CancellationToken cancellationToken)
    {
        await _authorizations.TryRevokeAsync(authorization, cancellationToken);
        await _tokens.RevokeByAuthorizationIdAsync(
            (await _authorizations.GetIdAsync(authorization, cancellationToken))!, cancellationToken);
    }

    private async Task<OAuthAuthorizationDto> ToDtoAsync(object authorization, object application, CancellationToken cancellationToken)
    {
        var scopes = await _authorizations.GetScopesAsync(authorization, cancellationToken);
        var clientId = await _applications.GetClientIdAsync(application, cancellationToken) ?? string.Empty;

        return new OAuthAuthorizationDto(
            Id: await _authorizations.GetIdAsync(authorization, cancellationToken) ?? string.Empty,
            ClientId: clientId,
            ClientName: await _applications.GetDisplayNameAsync(application, cancellationToken) ?? clientId,
            RedirectHosts: await ConsentedHostsAsync(authorization, cancellationToken),
            Scopes: OAuthScopes.Grantable.Where(scope => scopes.Contains(scope, StringComparer.Ordinal)).ToList(),
            CreatedAt: (await _authorizations.GetCreationDateAsync(authorization, cancellationToken))?.UtcDateTime ?? DateTime.MinValue);
    }
}
