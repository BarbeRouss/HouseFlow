namespace HouseFlow.Application.OAuth;

/// <summary>
/// Scopes of the embedded OAuth 2.1 authorization server (issue #304): what a third-party client
/// (Claude, through the MCP server) may be granted on a user's data.
/// </summary>
public static class OAuthScopes
{
    /// <summary>Read the user's houses, devices and maintenance records.</summary>
    public const string HousesRead = "houses:read";

    /// <summary>Create and modify the user's houses, devices and maintenance records.</summary>
    public const string HousesWrite = "houses:write";

    /// <summary>
    /// Protocol scope that makes OpenIddict issue a refresh token. Added by the server to every
    /// code flow of a client allowed to refresh; never shown to the user, never part of a consent.
    /// </summary>
    public const string OfflineAccess = "offline_access";

    /// <summary>The scopes a user can grant to a client, in display order.</summary>
    public static IReadOnlyList<string> Grantable { get; } = [HousesRead, HousesWrite];

    public static bool IsGrantable(string? scope) =>
        scope is not null && Grantable.Contains(scope, StringComparer.Ordinal);
}
