using System.Text.Json;
using HouseFlow.Application.DTOs;
using HouseFlow.Application.OAuth;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace HouseFlow.API.OAuth;

/// <summary>
/// OAuth clients (registered by DCR) and the users' consents to them — permanent OpenIddict
/// authorizations, at most one valid per (user, client) in practice: a new consent widens the
/// existing one. Shared by the authorization endpoint and the <c>/api/v1/oauth</c> screens.
/// </summary>
public sealed class OAuthConsentService
{
    /// <summary>Unix time (seconds) of the registration, returned as <c>client_id_issued_at</c> (RFC 7591).</summary>
    public const string ClientIdIssuedAtProperty = "client_id_issued_at";

    /// <summary>Self-declared home page of the client (RFC 7591 <c>client_uri</c>).</summary>
    public const string ClientUriProperty = "client_uri";

    private readonly IOpenIddictApplicationManager _applications;
    private readonly IOpenIddictAuthorizationManager _authorizations;
    private readonly IOpenIddictTokenManager _tokens;

    public OAuthConsentService(
        IOpenIddictApplicationManager applications,
        IOpenIddictAuthorizationManager authorizations,
        IOpenIddictTokenManager tokens)
    {
        _applications = applications;
        _authorizations = authorizations;
        _tokens = tokens;
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
        var redirectUris = await _applications.GetRedirectUrisAsync(application, cancellationToken);

        return new OAuthClientInfoDto(
            ClientId: clientId,
            ClientName: await _applications.GetDisplayNameAsync(application, cancellationToken) ?? clientId,
            ClientUri: properties.TryGetValue(ClientUriProperty, out var clientUri) && clientUri.ValueKind == JsonValueKind.String
                ? clientUri.GetString()
                : null,
            // host[:port] only: the part of a redirect URI the user can judge.
            RedirectHosts: redirectUris
                .Select(uri => Uri.TryCreate(uri, UriKind.Absolute, out var parsed) ? parsed.Authority : null)
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Scopes: await GetClientScopesAsync(application, cancellationToken));
    }

    /// <summary>
    /// The user's valid permanent authorization for the client (the most recent one, should a race
    /// have created two) and the scopes the user granted to the client across them.
    /// </summary>
    public async Task<(string? AuthorizationId, IReadOnlyList<string> GrantedScopes)> FindConsentAsync(
        Guid userId, object application, CancellationToken cancellationToken = default)
    {
        var (authorization, granted) = await FindValidAuthorizationAsync(userId, application, cancellationToken);
        return (authorization is null ? null : await _authorizations.GetIdAsync(authorization, cancellationToken), granted);
    }

    private async Task<(object? Authorization, IReadOnlyList<string> GrantedScopes)> FindValidAuthorizationAsync(
        Guid userId, object application, CancellationToken cancellationToken)
    {
        var applicationId = await _applications.GetIdAsync(application, cancellationToken);

        object? latest = null;
        DateTimeOffset? latestCreation = null;
        var granted = new HashSet<string>(StringComparer.Ordinal);

        await foreach (var authorization in _authorizations.FindAsync(
            subject: userId.ToString(), client: applicationId, status: Statuses.Valid,
            type: AuthorizationTypes.Permanent, scopes: null, cancellationToken))
        {
            granted.UnionWith(await _authorizations.GetScopesAsync(authorization, cancellationToken));

            var creation = await _authorizations.GetCreationDateAsync(authorization, cancellationToken);
            if (latest is null || creation > latestCreation)
            {
                latest = authorization;
                latestCreation = creation;
            }
        }

        return (latest, OAuthScopes.Grantable.Where(granted.Contains).ToList());
    }

    /// <summary>Records the user's consent: widens the existing authorization, or creates it.</summary>
    public async Task<OAuthAuthorizationDto> GrantAsync(
        Guid userId, object application, IReadOnlyCollection<string> scopes, CancellationToken cancellationToken = default)
    {
        var (authorization, _) = await FindValidAuthorizationAsync(userId, application, cancellationToken);

        if (authorization is not null)
        {
            var descriptor = new OpenIddictAuthorizationDescriptor();
            await _authorizations.PopulateAsync(descriptor, authorization, cancellationToken);
            descriptor.Scopes.UnionWith(scopes);
            await _authorizations.UpdateAsync(authorization, descriptor, cancellationToken);
        }
        else
        {
            var descriptor = new OpenIddictAuthorizationDescriptor
            {
                ApplicationId = await _applications.GetIdAsync(application, cancellationToken),
                CreationDate = DateTimeOffset.UtcNow,
                Status = Statuses.Valid,
                Subject = userId.ToString(),
                Type = AuthorizationTypes.Permanent
            };
            descriptor.Scopes.UnionWith(scopes);
            authorization = await _authorizations.CreateAsync(descriptor, cancellationToken);
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

        await _authorizations.TryRevokeAsync(authorization, cancellationToken);
        await _tokens.RevokeByAuthorizationIdAsync(authorizationId, cancellationToken);
        return true;
    }

    private async Task<OAuthAuthorizationDto> ToDtoAsync(object authorization, object application, CancellationToken cancellationToken)
    {
        var scopes = await _authorizations.GetScopesAsync(authorization, cancellationToken);
        var clientId = await _applications.GetClientIdAsync(application, cancellationToken) ?? string.Empty;

        return new OAuthAuthorizationDto(
            Id: await _authorizations.GetIdAsync(authorization, cancellationToken) ?? string.Empty,
            ClientId: clientId,
            ClientName: await _applications.GetDisplayNameAsync(application, cancellationToken) ?? clientId,
            Scopes: OAuthScopes.Grantable.Where(scope => scopes.Contains(scope, StringComparer.Ordinal)).ToList(),
            CreatedAt: (await _authorizations.GetCreationDateAsync(authorization, cancellationToken))?.UtcDateTime ?? DateTime.MinValue);
    }
}
