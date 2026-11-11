using System.Text.Json;
using HouseFlow.Application.OAuth;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace HouseFlow.API.OAuth;

/// <summary>
/// What an OAuth client is in the OpenIddict store, whether it registered itself (DCR, RFC 7591) or
/// is described by a Client ID Metadata Document (#308): one public client, authorization code flow
/// with PKCE, consent always explicit, the scopes and redirect URIs of its validated metadata.
/// </summary>
public static class OAuthClientDescriptors
{
    /// <summary>Sets everything <paramref name="registration"/> decides, replacing what a refreshed client had.</summary>
    public static void Apply(OpenIddictApplicationDescriptor descriptor, ValidatedClientRegistration registration)
    {
        // Loopback-only clients are native apps (RFC 8252): OpenIddict then accepts any port on
        // a loopback redirect URI registered without one (§7.3).
        descriptor.ApplicationType = registration.RedirectUris.All(uri => RedirectUriPolicy.IsLoopback(new Uri(uri)))
            ? ApplicationTypes.Native
            : ApplicationTypes.Web;
        descriptor.ClientType = ClientTypes.Public;
        descriptor.ConsentType = ConsentTypes.Explicit;
        descriptor.DisplayName = registration.ClientName;

        descriptor.Requirements.Clear();
        descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);

        descriptor.Permissions.Clear();
        descriptor.Permissions.Add(Permissions.Endpoints.Authorization);
        descriptor.Permissions.Add(Permissions.Endpoints.Token);
        descriptor.Permissions.Add(Permissions.Endpoints.Revocation);
        descriptor.Permissions.Add(Permissions.GrantTypes.AuthorizationCode);
        descriptor.Permissions.Add(Permissions.ResponseTypes.Code);
        if (registration.AllowsRefreshTokens)
            descriptor.Permissions.Add(Permissions.GrantTypes.RefreshToken);
        foreach (var scope in registration.Scopes)
            descriptor.Permissions.Add(Permissions.Prefixes.Scope + scope);

        descriptor.RedirectUris.Clear();
        foreach (var uri in registration.RedirectUris)
            descriptor.RedirectUris.Add(new Uri(uri, UriKind.Absolute));

        if (registration.ClientUri is not null)
            descriptor.Properties[OAuthConsentService.ClientUriProperty] = JsonSerializer.SerializeToElement(registration.ClientUri);
        else
            descriptor.Properties.Remove(OAuthConsentService.ClientUriProperty);
    }
}
