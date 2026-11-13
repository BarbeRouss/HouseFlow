using System.Net;
using System.Text.Json;
using FluentAssertions;
using HouseFlow.Application.Common;
using HouseFlow.Core.Entities;
using HouseFlow.Infrastructure.Data;
using HouseFlow.Infrastructure.Jobs;
using HouseFlow.IntegrationTests.OAuth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenIddict.EntityFrameworkCore.Models;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace HouseFlow.IntegrationTests.Gdpr;

/// <summary>
/// RGPD (issue #307) — the data of the AI clients connected through MCP: the retention job purges
/// what is expired or unused, account erasure removes the user's grants, the export lists them, and
/// the MCP call log follows the audit log retention. Integration tests: the job and the erasure rely on
/// <c>ExecuteDelete</c>, which the InMemory provider does not support.
/// </summary>
[Collection("Integration")]
public class OAuthRetentionTests
{
    private static readonly DataRetentionOptions Defaults = new();

    private readonly IntegrationTestFixture _fixture;
    private readonly OAuthTestClient _oauth;

    public OAuthRetentionTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _oauth = new OAuthTestClient(fixture);
    }

    private static DataRetentionJob CreateJob(HouseFlowDbContext context) =>
        new(context, Options.Create(new DataRetentionOptions()), NullLogger<DataRetentionJob>.Instance, TimeProvider.System);

    private static string NewId() => Guid.NewGuid().ToString();

    private static OpenIddictEntityFrameworkCoreApplication NewClient(DateTime? issuedAt) => new()
    {
        Id = NewId(),
        ClientId = $"retention-{Guid.NewGuid():N}",
        DisplayName = "Retention test client",
        ConcurrencyToken = NewId(),
        Properties = issuedAt is { } at
            ? JsonSerializer.Serialize(new Dictionary<string, long> { ["client_id_issued_at"] = new DateTimeOffset(at, TimeSpan.Zero).ToUnixTimeSeconds() })
            : null
    };

    private static OpenIddictEntityFrameworkCoreAuthorization NewAuthorization(
        OpenIddictEntityFrameworkCoreApplication client, string subject, string status, DateTime creation) => new()
    {
        Id = NewId(),
        Application = client,
        Subject = subject,
        Status = status,
        Type = AuthorizationTypes.Permanent,
        CreationDate = creation,
        ConcurrencyToken = NewId()
    };

    private static OpenIddictEntityFrameworkCoreToken NewToken(
        OpenIddictEntityFrameworkCoreApplication client, OpenIddictEntityFrameworkCoreAuthorization? authorization,
        string subject, string status, DateTime creation, DateTime expiration) => new()
    {
        Id = NewId(),
        Application = client,
        Authorization = authorization,
        Subject = subject,
        Status = status,
        Type = TokenTypeHints.RefreshToken,
        CreationDate = creation,
        ExpirationDate = expiration,
        ConcurrencyToken = NewId()
    };

    private async Task<(bool Client, bool Authorization, bool Token)> ExistAsync(
        string? clientId, string? authorizationId, string? tokenId)
    {
        await using var db = await _fixture.CreateDbContextAsync();
        return (
            clientId is not null && await db.Set<OpenIddictEntityFrameworkCoreApplication>().AnyAsync(a => a.Id == clientId),
            authorizationId is not null && await db.Set<OpenIddictEntityFrameworkCoreAuthorization>().AnyAsync(a => a.Id == authorizationId),
            tokenId is not null && await db.Set<OpenIddictEntityFrameworkCoreToken>().AnyAsync(t => t.Id == tokenId));
    }

    // ------------------------------------------------------------------ Retention job

    [Fact]
    public async Task Job_PurgesExpiredAndRevokedTokens_AndKeepsTheActiveOnes()
    {
        await using var context = await _fixture.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        var old = now.AddDays(-Defaults.RevokedRefreshTokenRetentionDays - 5);
        var subject = NewId();

        // A client with a valid consent, so that the client itself stays out of the picture.
        var client = NewClient(now);
        var consent = NewAuthorization(client, subject, Statuses.Valid, old);
        var active = NewToken(client, consent, subject, Statuses.Valid, now.AddDays(-1), now.AddDays(29));
        var expired = NewToken(client, consent, subject, Statuses.Valid, old.AddDays(-30), old);
        var revoked = NewToken(client, consent, subject, Statuses.Revoked, old, now.AddDays(10));
        var revokedRecently = NewToken(client, consent, subject, Statuses.Revoked, now.AddDays(-2), now.AddDays(28));
        context.AddRange(client, consent, active, expired, revoked, revokedRecently);
        await context.SaveChangesAsync();

        await CreateJob(context).ExecuteAsync();

        (await ExistAsync(client.Id, consent.Id, active.Id)).Should().Be((true, true, true), "a live token under a valid consent stays");
        (await ExistAsync(null, null, expired.Id)).Token.Should().BeFalse("it expired more than 30 days ago");
        (await ExistAsync(null, null, revoked.Id)).Token.Should().BeFalse("it was revoked and created more than 30 days ago");
        (await ExistAsync(null, null, revokedRecently.Id)).Token.Should().BeTrue("a recently revoked token is kept for reuse detection");
    }

    [Fact]
    public async Task Job_PurgesARevokedAuthorizationOnceItsTokensAreGone_AndNeverAValidOne()
    {
        await using var context = await _fixture.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        var old = now.AddDays(-Defaults.RevokedRefreshTokenRetentionDays - 5);
        var subject = NewId();

        var client = NewClient(now);
        var valid = NewAuthorization(client, subject, Statuses.Valid, old);
        var revoked = NewAuthorization(client, subject, Statuses.Revoked, old);
        var revokedToken = NewToken(client, revoked, subject, Statuses.Revoked, old, old.AddDays(30));
        var stillReferenced = NewAuthorization(client, subject, Statuses.Revoked, old);
        var youngToken = NewToken(client, stillReferenced, subject, Statuses.Revoked, now.AddDays(-1), now.AddDays(29));
        context.AddRange(client, valid, revoked, revokedToken, stillReferenced, youngToken);
        await context.SaveChangesAsync();

        // One pass removes the old token, and then the authorization that no token references any more.
        await CreateJob(context).ExecuteAsync();

        (await ExistAsync(null, valid.Id, null)).Authorization.Should().BeTrue("a valid consent is never purged");
        (await ExistAsync(null, revoked.Id, revokedToken.Id)).Should().Be((false, false, false));
        (await ExistAsync(null, stillReferenced.Id, youngToken.Id)).Should().Be((false, true, true),
            "an authorization is kept while a token under it is");
    }

    [Fact]
    public async Task Job_PurgesAnUnusedClientOlderThan30Days_AndKeepsTheOthers()
    {
        await using var context = await _fixture.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        var old = now.AddDays(-Defaults.UnusedOAuthClientRetentionDays - 5);
        var subject = NewId();

        var unusedOld = NewClient(old);
        var unusedYoung = NewClient(now.AddDays(-2));
        var undated = NewClient(null);

        // Registered long ago but used: it has a valid consent, and a live token.
        var usedOld = NewClient(old);
        var consent = NewAuthorization(usedOld, subject, Statuses.Valid, old);
        var liveToken = NewToken(usedOld, consent, subject, Statuses.Valid, now.AddDays(-1), now.AddDays(29));

        // Registered long ago, its only consent revoked since: unused, and its leftovers go with it.
        var revokedOld = NewClient(old);
        var revokedConsent = NewAuthorization(revokedOld, subject, Statuses.Revoked, now.AddDays(-3));
        var revokedToken = NewToken(revokedOld, revokedConsent, subject, Statuses.Revoked, now.AddDays(-3), now.AddDays(27));

        context.AddRange(unusedOld, unusedYoung, undated, usedOld, consent, liveToken, revokedOld, revokedConsent, revokedToken);
        await context.SaveChangesAsync();

        await CreateJob(context).ExecuteAsync();

        (await ExistAsync(unusedOld.Id, null, null)).Client.Should().BeFalse("never authorized, registered more than 30 days ago");
        (await ExistAsync(undated.Id, null, null)).Client.Should().BeFalse("a client that cannot be dated is treated as old");
        (await ExistAsync(unusedYoung.Id, null, null)).Client.Should().BeTrue("registered 2 days ago: it may still be consented to");
        (await ExistAsync(usedOld.Id, consent.Id, liveToken.Id)).Should().Be((true, true, true),
            "a client with a valid authorization is never touched, however old");
        (await ExistAsync(revokedOld.Id, revokedConsent.Id, revokedToken.Id)).Should().Be((false, false, false));
    }

    [Fact]
    public async Task Job_AnonymizesThenPurgesTheMcpCallLog_LikeAnyAuditLine()
    {
        await using var context = await _fixture.CreateDbContextAsync();
        var now = DateTime.UtcNow;

        AuditLog Call(DateTime at) => new()
        {
            Id = Guid.NewGuid(),
            EntityType = "McpTool",
            EntityId = "list_houses",
            Action = "McpCall",
            UserId = Guid.NewGuid(),
            Username = "someone@example.com",
            IpAddress = "203.0.113.42",
            UserAgent = "Claude-User",
            Timestamp = at
        };

        var recent = Call(now.AddDays(-1));
        var past1Year = Call(now.AddDays(-Defaults.AuditLogAnonymizeAfterDays - 1));
        var past3Years = Call(now.AddDays(-Defaults.AuditLogDeleteAfterDays - 1));
        context.AuditLogs.AddRange(recent, past1Year, past3Years);
        await context.SaveChangesAsync();

        await CreateJob(context).ExecuteAsync();

        await using var verify = await _fixture.CreateDbContextAsync();
        var logs = await verify.AuditLogs.AsNoTracking()
            .Where(a => new[] { recent.Id, past1Year.Id, past3Years.Id }.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id);

        logs[recent.Id].UserId.Should().NotBeNull("an MCP call from yesterday is still identifying (1 year)");
        logs[recent.Id].UserAgent.Should().Be("Claude-User");

        var anonymized = logs[past1Year.Id];
        anonymized.UserId.Should().BeNull();
        anonymized.Username.Should().BeNull();
        anonymized.IpAddress.Should().BeNull();
        anonymized.UserAgent.Should().BeNull();
        anonymized.EntityId.Should().Be("list_houses", "the tool name is not personal data and feeds the statistics");

        logs.Should().NotContainKey(past3Years.Id, "audit lines are purged after 3 years");
    }

    // --------------------------------------------------------------- Account erasure

    private async Task<HttpResponseMessage> DeleteAccountAsync(TestUser user)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/users/me")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new DeleteAccountRequestDto(password: "Password123!")),
            Headers = { Authorization = new("Bearer", user.AccessToken) }
        };
        return await _oauth.Http.SendAsync(request);
    }

    [Fact]
    public async Task AccountErasure_RemovesEveryGrantOfTheUser_AndTheirAccessTokenIsRefusedOnMcp()
    {
        var user = await _oauth.RegisterUserAsync();
        var other = await _oauth.RegisterUserAsync();
        var clientId = await _oauth.RegisterClientAsync("Erased app");
        var tokens = await _oauth.ConnectAsync(user, clientId);
        await _oauth.ConnectAsync(other, clientId);

        (await _oauth.SendAsBearer(HttpMethod.Post, "/mcp", tokens.AccessToken)).StatusCode
            .Should().NotBe(HttpStatusCode.Unauthorized, "the token works before the account is erased");

        (await DeleteAccountAsync(user)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db = await _fixture.CreateDbContextAsync();
        var subject = user.Id.ToString();
        (await db.Set<OpenIddictEntityFrameworkCoreAuthorization>().CountAsync(a => a.Subject == subject)).Should().Be(0);
        (await db.Set<OpenIddictEntityFrameworkCoreToken>().CountAsync(t => t.Subject == subject)).Should().Be(0);
        (await db.Set<OpenIddictEntityFrameworkCoreAuthorization>().CountAsync(a => a.Subject == other.Id.ToString()))
            .Should().Be(1, "another user's consent to the same client is untouched");

        (await _oauth.SendAsBearer(HttpMethod.Post, "/mcp", tokens.AccessToken)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized, "the access token is unexpired, but its entry is gone");
    }

    // ------------------------------------------------------------------------ Export

    [Fact]
    public async Task Export_ListsTheConnectedApplication_WithoutAnyToken()
    {
        var user = await _oauth.RegisterUserAsync();
        var clientId = await _oauth.RegisterClientAsync("Exported app");
        var tokens = await _oauth.ConnectAsync(user, clientId, "houses:read", "houses:read");

        var response = await _oauth.SendAsBearer(HttpMethod.Get, "/api/v1/users/me/export", user.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var app = json.RootElement.GetProperty("connectedApplications").EnumerateArray().Should().ContainSingle().Subject;
        app.GetProperty("clientId").GetString().Should().Be(clientId);
        app.GetProperty("clientName").GetString().Should().Be("Exported app");
        app.GetProperty("redirectHosts").EnumerateArray().Select(e => e.GetString()).Should().Equal("127.0.0.1:9");
        app.GetProperty("scopes").EnumerateArray().Select(e => e.GetString()).Should().Equal("houses:read");
        app.GetProperty("consentedAt").GetDateTime().Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(5));

        body.Should().NotContain(tokens.AccessToken).And.NotContain(tokens.RefreshToken);
    }
}
