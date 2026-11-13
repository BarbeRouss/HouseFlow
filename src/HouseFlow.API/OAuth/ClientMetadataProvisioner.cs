using System.Text.Json;
using HouseFlow.Application.OAuth;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

namespace HouseFlow.API.OAuth;

/// <summary>Whether a Client ID Metadata Document client is usable, and if not what to tell the caller.</summary>
public readonly record struct ClientMetadataOutcome(bool Succeeded, string? Description)
{
    public static ClientMetadataOutcome Ok { get; } = new(true, null);

    public static ClientMetadataOutcome Refused(string description) => new(false, description);
}

/// <summary>
/// Turns a <c>client_id</c> that is a URL (issue #308) into an OpenIddict application, before
/// OpenIddict looks the client up. The application row is the cache: <c>cimd_expires_at</c> says
/// until when the stored copy of the document is trusted, so every replica shares it.
/// <list type="bullet">
/// <item>Within the TTL: nothing is downloaded.</item>
/// <item>Past it: the document is downloaded again and the application updated (a removed redirect
/// URI is refused from then on; tokens already issued live until their normal revocation). A failure
/// — network, status, invalid document — <b>refuses</b> the authorization: no stale copy is used.</item>
/// </list>
/// Only <c>/connect/authorize</c> calls this: the token endpoint never downloads anything, so that
/// issuing tokens does not depend on a third party being up.
/// </summary>
public sealed class ClientMetadataProvisioner
{
    /// <summary>Property of the application: it was created from a metadata document (shared by all users).</summary>
    public const string ClientIdMetadataProperty = "cimd";

    /// <summary>Unix time (seconds) of the last download.</summary>
    public const string FetchedAtProperty = "cimd_fetched_at";

    /// <summary>Unix time (seconds) until which the stored copy of the document is trusted.</summary>
    public const string ExpiresAtProperty = "cimd_expires_at";

    private const string Unusable = "The client metadata document could not be retrieved or is not valid.";

    private readonly IOpenIddictApplicationManager _applications;
    private readonly IClientMetadataDocumentFetcher _fetcher;
    private readonly TimeProvider _time;
    private readonly ILogger<ClientMetadataProvisioner> _logger;

    public ClientMetadataProvisioner(
        IOpenIddictApplicationManager applications,
        IClientMetadataDocumentFetcher fetcher,
        TimeProvider time,
        ILogger<ClientMetadataProvisioner> logger)
    {
        _applications = applications;
        _fetcher = fetcher;
        _time = time;
        _logger = logger;
    }

    public async Task<ClientMetadataOutcome> EnsureAsync(
        string clientId, IReadOnlyCollection<Uri> reservedOrigins, string callerKey, CancellationToken cancellationToken)
    {
        if (!ClientIdMetadataUrl.TryParse(clientId, out var url))
            return ClientMetadataOutcome.Refused("The client_id is not a valid client metadata document URL (https, default port, no fragment).");

        var now = _time.GetUtcNow();
        var existing = await _applications.FindByClientIdAsync(clientId, cancellationToken);
        if (existing is not null)
        {
            var properties = await _applications.GetPropertiesAsync(existing, cancellationToken);
            if (!IsTrue(properties, ClientIdMetadataProperty))
                return ClientMetadataOutcome.Refused(Unusable);
            if (Unix(properties, ExpiresAtProperty) is { } expiresAt && expiresAt > now.ToUnixTimeSeconds())
                return ClientMetadataOutcome.Ok;
        }

        var fetched = await _fetcher.FetchAsync(url, callerKey, cancellationToken);
        if (!fetched.Succeeded)
            return ClientMetadataOutcome.Refused(Unusable);

        if (!ClientMetadataDocumentValidator.TryValidate(url, fetched.Document, reservedOrigins, out var registration, out var error))
        {
            _logger.LogWarning("Client metadata document of {Host} refused: {Reason}", url.IdnHost, error);
            return ClientMetadataOutcome.Refused(Unusable);
        }

        var expires = now + ClientMetadataCachePolicy.Ttl(fetched.MaxAge);
        if (existing is not null)
        {
            await UpdateAsync(existing, registration, now, expires, cancellationToken);
            return ClientMetadataOutcome.Ok;
        }

        var descriptor = new OpenIddictApplicationDescriptor { ClientId = clientId };
        OAuthClientDescriptors.Apply(descriptor, registration);
        Stamp(descriptor, now, expires);
        descriptor.Properties[OAuthConsentService.ClientIdIssuedAtProperty] = JsonSerializer.SerializeToElement(now.ToUnixTimeSeconds());

        try
        {
            await _applications.CreateAsync(descriptor, cancellationToken);
            _logger.LogInformation("OAuth client metadata document client registered: {Host}", url.IdnHost);
        }
        catch (Exception ex) when (ex is OpenIddictExceptions.ValidationException or DbUpdateException)
        {
            // Two first authorizations raced on the unique ClientId: the other one created it.
            if (await _applications.FindByClientIdAsync(clientId, cancellationToken) is not { } created)
                throw;
            await UpdateAsync(created, registration, now, expires, cancellationToken);
        }

        return ClientMetadataOutcome.Ok;
    }

    private async Task UpdateAsync(
        object application, ValidatedClientRegistration registration, DateTimeOffset now, DateTimeOffset expires,
        CancellationToken cancellationToken)
    {
        var descriptor = new OpenIddictApplicationDescriptor();
        await _applications.PopulateAsync(descriptor, application, cancellationToken);
        OAuthClientDescriptors.Apply(descriptor, registration);
        Stamp(descriptor, now, expires);
        await _applications.UpdateAsync(application, descriptor, cancellationToken);
    }

    private static void Stamp(OpenIddictApplicationDescriptor descriptor, DateTimeOffset now, DateTimeOffset expires)
    {
        descriptor.Properties[ClientIdMetadataProperty] = JsonSerializer.SerializeToElement(true);
        descriptor.Properties[FetchedAtProperty] = JsonSerializer.SerializeToElement(now.ToUnixTimeSeconds());
        descriptor.Properties[ExpiresAtProperty] = JsonSerializer.SerializeToElement(expires.ToUnixTimeSeconds());
    }

    public static bool IsTrue(IReadOnlyDictionary<string, JsonElement> properties, string name) =>
        properties.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static long? Unix(IReadOnlyDictionary<string, JsonElement> properties, string name) =>
        properties.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var seconds)
            ? seconds
            : null;
}
