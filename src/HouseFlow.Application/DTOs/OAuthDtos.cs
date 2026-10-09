using System.ComponentModel.DataAnnotations;

namespace HouseFlow.Application.DTOs;

/// <summary>
/// An OAuth client as shown on the consent screen. <see cref="RedirectHosts"/> (host[:port] of the
/// registered redirect URIs) is where the user is sent back — the only trustworthy identity of a
/// dynamically registered client, whose name is self-declared.
/// </summary>
public record OAuthClientInfoDto(
    string ClientId,
    string ClientName,
    string? ClientUri,
    IReadOnlyList<string> RedirectHosts,
    IReadOnlyList<string> Scopes);

/// <summary>A valid consent of the user to an OAuth client (a permanent OpenIddict authorization).</summary>
public record OAuthAuthorizationDto(
    string Id,
    string ClientId,
    string ClientName,
    IReadOnlyList<string> Scopes,
    DateTime CreatedAt);

/// <summary>Consent given on the consent screen: the scopes the user left checked.</summary>
public record GrantOAuthAuthorizationRequestDto(
    [Required(ErrorMessage = "clientId is required")]
    string ClientId,

    [Required(ErrorMessage = "scopes is required")]
    IReadOnlyList<string> Scopes
);
