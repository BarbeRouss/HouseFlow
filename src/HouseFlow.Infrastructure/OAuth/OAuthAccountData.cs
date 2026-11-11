using System.Text.Json;
using HouseFlow.Application.DTOs;
using HouseFlow.Application.Interfaces;
using HouseFlow.Application.OAuth;
using HouseFlow.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore.Models;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace HouseFlow.Infrastructure.OAuth;

/// <summary>
/// The user's OAuth data (issue #307) read and erased straight in the OpenIddict tables.
/// Writes use <c>ExecuteDeleteAsync</c>: they join the caller's transaction (same
/// <see cref="HouseFlowDbContext"/> instance) and bypass the change tracker, so that erasing a
/// consent does not write an audit line per token.
/// </summary>
public sealed class OAuthAccountData : IOAuthAccountData
{
    private readonly HouseFlowDbContext _context;

    public OAuthAccountData(HouseFlowDbContext context) => _context = context;

    public async Task<IReadOnlyList<ExportConnectedApplicationDto>> ListConnectedApplicationsAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var subject = userId.ToString();

        // Project only what the export shows: never the tokens, never the client secret.
        var rows = await _context.Set<OpenIddictEntityFrameworkCoreAuthorization>()
            .AsNoTracking()
            .Where(a => a.Subject == subject && a.Status == Statuses.Valid && a.Type == AuthorizationTypes.Permanent)
            .Select(a => new
            {
                ClientId = a.Application!.ClientId,
                a.Application.DisplayName,
                a.Scopes,
                a.Properties,
                a.CreationDate
            })
            .ToListAsync(cancellationToken);

        return rows
            .OrderByDescending(row => row.CreationDate)
            .Select(row => new ExportConnectedApplicationDto(
                ClientId: row.ClientId ?? string.Empty,
                ClientName: row.DisplayName ?? row.ClientId ?? string.Empty,
                RedirectHosts: ReadStrings(row.Properties, OAuthProperties.RedirectHosts),
                Scopes: OAuthScopes.Grantable.Where(scope => ReadStrings(row.Scopes).Contains(scope, StringComparer.Ordinal)).ToList(),
                ConsentedAt: row.CreationDate is { } created ? DateTime.SpecifyKind(created, DateTimeKind.Utc) : null))
            .ToList();
    }

    public async Task DeleteAllAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var subject = userId.ToString();
        var authorizations = _context.Set<OpenIddictEntityFrameworkCoreAuthorization>();
        var tokens = _context.Set<OpenIddictEntityFrameworkCoreToken>();

        var authorizationIds = await authorizations
            .Where(a => a.Subject == subject)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);

        // Tokens first: they reference the authorizations.
        await tokens
            .Where(t => t.Subject == subject || authorizationIds.Contains(EF.Property<string>(t, "AuthorizationId")))
            .ExecuteDeleteAsync(cancellationToken);

        await authorizations
            .Where(a => a.Subject == subject)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>The strings of a JSON array, or of the array held by a property of a JSON object; empty if absent or malformed.</summary>
    private static IReadOnlyList<string> ReadStrings(string? json, string? property = null)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            using var document = JsonDocument.Parse(json);
            var node = document.RootElement;
            if (property is not null &&
                (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(property, out node)))
                return [];

            return node.ValueKind == JsonValueKind.Array
                ? node.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList()
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
