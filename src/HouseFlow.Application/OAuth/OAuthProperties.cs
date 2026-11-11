namespace HouseFlow.Application.OAuth;

/// <summary>
/// Names of the custom properties the authorization server stores in the <c>Properties</c> JSON of
/// OpenIddict applications and authorizations. Shared by the API (which writes them) and by the
/// persistence layer (which reads them for the GDPR export and the retention job).
/// </summary>
public static class OAuthProperties
{
    /// <summary>Unix time (seconds) of the registration, returned as <c>client_id_issued_at</c> (RFC 7591).</summary>
    public const string ClientIdIssuedAt = "client_id_issued_at";

    /// <summary>Self-declared home page of the client (RFC 7591 <c>client_uri</c>).</summary>
    public const string ClientUri = "client_uri";

    /// <summary>
    /// Property of an authorization: the redirect hosts (JSON array) the user consented to send
    /// codes to — those the consent screen showed.
    /// </summary>
    public const string RedirectHosts = "redirect_hosts";
}
