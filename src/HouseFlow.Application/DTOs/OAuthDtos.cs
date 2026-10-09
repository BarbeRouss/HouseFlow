using System.ComponentModel.DataAnnotations;

namespace HouseFlow.Application.DTOs;

/// <summary>
/// An OAuth client as shown on the consent screen. <see cref="RedirectHosts"/> (hosts of the
/// registered redirect URIs, see <c>RedirectUriPolicy.DisplayHost</c>) is where the user is sent
/// back — the only trustworthy identity of a dynamically registered client, whose name is self-declared.
/// </summary>
public record OAuthClientInfoDto(
    string ClientId,
    string ClientName,
    string? ClientUri,
    IReadOnlyList<string> RedirectHosts,
    IReadOnlyList<string> Scopes);

/// <summary>
/// A valid consent of the user to an OAuth client (a permanent OpenIddict authorization), with the
/// redirect hosts it was given for — where the client receives access, which « Applications
/// connectées » shows besides the self-declared name.
/// </summary>
public record OAuthAuthorizationDto(
    string Id,
    string ClientId,
    string ClientName,
    IReadOnlyList<string> RedirectHosts,
    IReadOnlyList<string> Scopes,
    DateTime CreatedAt);

/// <summary>
/// Consent given on the consent screen: the scopes the user left checked, for the redirect URI of
/// the authorization request on screen — the consent covers its host only.
/// </summary>
public record GrantOAuthAuthorizationRequestDto(
    [Required(ErrorMessage = "clientId is required")]
    string ClientId,

    [Required(ErrorMessage = "scopes is required")]
    IReadOnlyList<string> Scopes,

    [Required(ErrorMessage = "redirectUri is required")]
    string RedirectUri
);
