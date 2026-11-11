using HouseFlow.Application.DTOs;

namespace HouseFlow.Application.Interfaces;

/// <summary>
/// The user's OAuth data (consents and tokens of the AI clients connected through MCP, issue #307),
/// kept by the embedded authorization server in the OpenIddict tables. A port: the Application layer
/// does not know OpenIddict. Implemented in HouseFlow.Infrastructure on the same
/// <see cref="IApplicationDbContext"/> instance, so that the writes join the caller's transaction.
/// </summary>
public interface IOAuthAccountData
{
    /// <summary>
    /// The user's valid consents (Art. 15 / 20) — client, redirect hosts, scopes, date; never a
    /// token nor an authorization code.
    /// </summary>
    Task<IReadOnlyList<ExportConnectedApplicationDto>> ListConnectedApplicationsAsync(
        Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Art. 17: removes every authorization and token of the user, whatever its status. A token
    /// that is no longer in the table is refused at once by the MCP server, even if unexpired.
    /// </summary>
    Task DeleteAllAsync(Guid userId, CancellationToken cancellationToken = default);
}
