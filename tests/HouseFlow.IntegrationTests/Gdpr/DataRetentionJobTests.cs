using FluentAssertions;
using HouseFlow.Application.Common;
using HouseFlow.Core.Entities;
using HouseFlow.Core.Enums;
using HouseFlow.Infrastructure.Data;
using HouseFlow.Infrastructure.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HouseFlow.IntegrationTests.Gdpr;

/// <summary>
/// RGPD Art. 5(1)(e) — vérifie que les durées de conservation annoncées sont réellement
/// appliquées en base. Tests d'intégration (et non unitaires) parce que le job repose sur
/// <c>ExecuteUpdate</c>/<c>ExecuteDelete</c>, que le provider InMemory ne supporte pas.
///
/// Chaque test utilise ses propres identifiants et ne compte que ses propres lignes, pour
/// rester indépendant des données laissées par les autres tests de la collection.
/// </summary>
[Collection("Integration")]
public class DataRetentionJobTests
{
    private readonly IntegrationTestFixture _fixture;
    private static readonly DataRetentionOptions Defaults = new();

    public DataRetentionJobTests(IntegrationTestFixture fixture) => _fixture = fixture;

    private static DataRetentionJob CreateJob(HouseFlowDbContext context, DataRetentionOptions? options = null) =>
        new(context,
            Options.Create(options ?? new DataRetentionOptions()),
            NullLogger<DataRetentionJob>.Instance,
            TimeProvider.System);

    private static async Task<User> SeedUserAsync(HouseFlowDbContext context)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"retention-{Guid.NewGuid():N}@example.com",
            FirstName = "Retention",
            LastName = "Test",
            PasswordHash = "not-a-real-hash",
            CreatedAt = DateTime.UtcNow
        };
        context.Users.Add(user);

        // SaveChanges écrit aussi des lignes d'audit — sans intérêt ici, les tests ne
        // comptent que les lignes qu'ils ont eux-mêmes identifiées.
        await context.SaveChangesAsync();
        return user;
    }

    // ------------------------------------------------------------ Anonymisation des IP

    [Fact]
    public async Task ExecuteAsync_ShouldTruncateAuditLogIpsOlderThanTheWindow_AndLeaveRecentOnesIntact()
    {
        await using var context = await _fixture.CreateDbContextAsync();

        var oldLog = NewAuditLog(DateTime.UtcNow.AddDays(-Defaults.IpAnonymizeAfterDays - 1), "203.0.113.42");
        var recentLog = NewAuditLog(DateTime.UtcNow.AddDays(-1), "203.0.113.42");
        var oldIpv6Log = NewAuditLog(DateTime.UtcNow.AddDays(-Defaults.IpAnonymizeAfterDays - 1), "2001:db8:85a3:8d3:1319:8a2e:370:7348");
        context.AuditLogs.AddRange(oldLog, recentLog, oldIpv6Log);
        await context.SaveChangesAsync();

        await CreateJob(context).ExecuteAsync();

        (await ReloadAsync(oldLog.Id)).IpAddress.Should().Be("203.0.113.0");
        (await ReloadAsync(oldIpv6Log.Id)).IpAddress.Should().Be("2001:db8:85a3::");
        (await ReloadAsync(recentLog.Id)).IpAddress.Should().Be("203.0.113.42", "les IP récentes restent utiles aux investigations");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldTruncateRefreshTokenAndApiKeyIps()
    {
        await using var context = await _fixture.CreateDbContextAsync();
        var user = await SeedUserAsync(context);

        var old = DateTime.UtcNow.AddDays(-Defaults.IpAnonymizeAfterDays - 1);

        // Le token est révoqué depuis longtemps, donc éligible aux deux règles. On allonge
        // ici la rétention des tokens révoqués pour observer l'anonymisation des IP :
        // avec les valeurs par défaut (30 j des deux côtés), un token révoqué est supprimé
        // avant que ses IP n'aient à être tronquées — la règle d'anonymisation reste une
        // défense en profondeur si la rétention des tokens venait à être allongée.
        var options = new DataRetentionOptions { RevokedRefreshTokenRetentionDays = 3650 };

        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = TokenHasher.Hash(Guid.NewGuid().ToString()),
            CreatedAt = old,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedByIp = "198.51.100.7",
            RevokedAt = old,
            RevokedByIp = "198.51.100.9"
        };

        var apiKey = new ApiKey
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Name = "retention-test",
            Prefix = $"hf_{Guid.NewGuid():N}"[..12],
            KeyHash = "not-a-real-hash",
            CreatedAt = old,
            CreatedByIp = "198.51.100.11"
        };

        context.RefreshTokens.Add(token);
        context.ApiKeys.Add(apiKey);
        await context.SaveChangesAsync();

        await CreateJob(context, options).ExecuteAsync();

        await using var verify = await _fixture.CreateDbContextAsync();
        var storedToken = await verify.RefreshTokens.AsNoTracking().SingleAsync(t => t.Id == token.Id);
        storedToken.CreatedByIp.Should().Be("198.51.100.0");
        storedToken.RevokedByIp.Should().Be("198.51.100.0");

        var storedKey = await verify.ApiKeys.AsNoTracking().SingleAsync(k => k.Id == apiKey.Id);
        storedKey.CreatedByIp.Should().Be("198.51.100.0");
    }

    // ---------------------------------------------------- Anonymisation des audit logs

    [Fact]
    public async Task ExecuteAsync_ShouldFullyAnonymizeAuditLogsPastTheIdentifyingWindow()
    {
        await using var context = await _fixture.CreateDbContextAsync();

        var log = NewAuditLog(DateTime.UtcNow.AddDays(-Defaults.AuditLogAnonymizeAfterDays - 1), "203.0.113.42");
        log.UserId = Guid.NewGuid();
        log.Username = "someone@example.com";
        log.UserAgent = "Mozilla/5.0";
        log.OldValues = """{"Email":"someone@example.com"}""";
        log.NewValues = """{"Email":"other@example.com"}""";
        log.ChangedProperties = """["Email"]""";
        context.AuditLogs.Add(log);
        await context.SaveChangesAsync();

        await CreateJob(context).ExecuteAsync();

        var stored = await ReloadAsync(log.Id);
        stored.UserId.Should().BeNull();
        stored.Username.Should().BeNull();
        stored.IpAddress.Should().BeNull();
        stored.UserAgent.Should().BeNull();
        stored.OldValues.Should().BeNull();
        stored.NewValues.Should().BeNull();
        stored.ChangedProperties.Should().BeNull();

        // Conservés pour les statistiques de sécurité : l'enregistrement est anonyme
        // (considérant 26), il reste exploitable sans se rapporter à une personne.
        stored.EntityType.Should().Be("House");
        stored.EntityId.Should().Be(log.EntityId);
        stored.Action.Should().Be("Modified");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldDeleteAuditLogsPastTheMaximumRetention()
    {
        await using var context = await _fixture.CreateDbContextAsync();

        var ancient = NewAuditLog(DateTime.UtcNow.AddDays(-Defaults.AuditLogDeleteAfterDays - 1), null);
        var justUnder = NewAuditLog(DateTime.UtcNow.AddDays(-Defaults.AuditLogDeleteAfterDays + 5), null);
        context.AuditLogs.AddRange(ancient, justUnder);
        await context.SaveChangesAsync();

        await CreateJob(context).ExecuteAsync();

        await using var verify = await _fixture.CreateDbContextAsync();
        (await verify.AuditLogs.AnyAsync(a => a.Id == ancient.Id)).Should().BeFalse();
        (await verify.AuditLogs.AnyAsync(a => a.Id == justUnder.Id)).Should().BeTrue();
    }

    // ------------------------------------------------------------ Tokens et clés API

    [Fact]
    public async Task ExecuteAsync_ShouldDeleteRevokedOrExpiredRefreshTokens_AndKeepActiveOnes()
    {
        await using var context = await _fixture.CreateDbContextAsync();
        var user = await SeedUserAsync(context);

        var past = DateTime.UtcNow.AddDays(-Defaults.RevokedRefreshTokenRetentionDays - 1);

        var revokedLongAgo = NewToken(user.Id, expiresAt: DateTime.UtcNow.AddDays(7), revokedAt: past);
        var expiredLongAgo = NewToken(user.Id, expiresAt: past);
        var revokedYesterday = NewToken(user.Id, expiresAt: DateTime.UtcNow.AddDays(7), revokedAt: DateTime.UtcNow.AddDays(-1));
        var active = NewToken(user.Id, expiresAt: DateTime.UtcNow.AddDays(7));

        context.RefreshTokens.AddRange(revokedLongAgo, expiredLongAgo, revokedYesterday, active);
        await context.SaveChangesAsync();

        await CreateJob(context).ExecuteAsync();

        await using var verify = await _fixture.CreateDbContextAsync();
        (await verify.RefreshTokens.AnyAsync(t => t.Id == revokedLongAgo.Id)).Should().BeFalse();
        (await verify.RefreshTokens.AnyAsync(t => t.Id == expiredLongAgo.Id)).Should().BeFalse();
        (await verify.RefreshTokens.AnyAsync(t => t.Id == revokedYesterday.Id)).Should().BeTrue("la fenêtre de détection de fraude court toujours");
        (await verify.RefreshTokens.AnyAsync(t => t.Id == active.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldDeleteRevokedApiKeys_AndKeepActiveOnes()
    {
        await using var context = await _fixture.CreateDbContextAsync();
        var user = await SeedUserAsync(context);

        var revoked = NewApiKey(user.Id, revokedAt: DateTime.UtcNow.AddDays(-Defaults.RevokedApiKeyRetentionDays - 1));
        var active = NewApiKey(user.Id, revokedAt: null);
        context.ApiKeys.AddRange(revoked, active);
        await context.SaveChangesAsync();

        await CreateJob(context).ExecuteAsync();

        await using var verify = await _fixture.CreateDbContextAsync();
        (await verify.ApiKeys.AnyAsync(k => k.Id == revoked.Id)).Should().BeFalse();
        (await verify.ApiKeys.AnyAsync(k => k.Id == active.Id)).Should().BeTrue();
    }

    // ----------------------------------------------------------------- Invitations

    [Fact]
    public async Task ExecuteAsync_ShouldExpirePendingInvitations_AndDeleteOldOnes()
    {
        await using var context = await _fixture.CreateDbContextAsync();
        var user = await SeedUserAsync(context);
        var house = new House { Id = Guid.NewGuid(), Name = "Retention house", UserId = user.Id, CreatedAt = DateTime.UtcNow };
        context.Houses.Add(house);
        await context.SaveChangesAsync();

        var pendingButPastDue = NewInvitation(house.Id, user.Id, InvitationStatus.Pending, DateTime.UtcNow.AddDays(-1));
        var pendingValid = NewInvitation(house.Id, user.Id, InvitationStatus.Pending, DateTime.UtcNow.AddDays(5));
        var longExpired = NewInvitation(house.Id, user.Id, InvitationStatus.Expired,
            DateTime.UtcNow.AddDays(-Defaults.ExpiredInvitationRetentionDays - 1));

        context.Invitations.AddRange(pendingButPastDue, pendingValid, longExpired);
        await context.SaveChangesAsync();

        await CreateJob(context).ExecuteAsync();

        await using var verify = await _fixture.CreateDbContextAsync();
        (await verify.Invitations.SingleAsync(i => i.Id == pendingButPastDue.Id)).Status.Should().Be(InvitationStatus.Expired);
        (await verify.Invitations.SingleAsync(i => i.Id == pendingValid.Id)).Status.Should().Be(InvitationStatus.Pending);
        (await verify.Invitations.AnyAsync(i => i.Id == longExpired.Id)).Should().BeFalse();
    }

    // ------------------------------------------------------ Idempotence et innocuité

    [Fact]
    public async Task ExecuteAsync_ShouldBeIdempotent()
    {
        await using var context = await _fixture.CreateDbContextAsync();
        var user = await SeedUserAsync(context);

        var log = NewAuditLog(DateTime.UtcNow.AddDays(-Defaults.IpAnonymizeAfterDays - 1), "203.0.113.42");
        var token = NewToken(user.Id, expiresAt: DateTime.UtcNow.AddDays(7));
        token.CreatedAt = DateTime.UtcNow.AddDays(-Defaults.IpAnonymizeAfterDays - 1);
        token.CreatedByIp = "198.51.100.7";

        context.AuditLogs.Add(log);
        context.RefreshTokens.Add(token);
        await context.SaveChangesAsync();

        var job = CreateJob(context);
        await job.ExecuteAsync();

        var afterFirstRun = await ReloadAsync(log.Id);
        await job.ExecuteAsync();
        var afterSecondRun = await ReloadAsync(log.Id);

        afterSecondRun.IpAddress.Should().Be(afterFirstRun.IpAddress).And.Be("203.0.113.0");

        await using var verify = await _fixture.CreateDbContextAsync();
        var storedToken = await verify.RefreshTokens.AsNoTracking().SingleAsync(t => t.Id == token.Id);
        storedToken.CreatedByIp.Should().Be("198.51.100.0");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNotTouchFreshData()
    {
        await using var context = await _fixture.CreateDbContextAsync();
        var user = await SeedUserAsync(context);

        var log = NewAuditLog(DateTime.UtcNow.AddHours(-1), "203.0.113.42");
        log.UserId = user.Id;
        log.Username = "fresh@example.com";
        log.UserAgent = "Mozilla/5.0";
        var token = NewToken(user.Id, expiresAt: DateTime.UtcNow.AddDays(7));
        token.CreatedByIp = "198.51.100.7";
        var apiKey = NewApiKey(user.Id, revokedAt: null);
        apiKey.CreatedByIp = "198.51.100.11";

        context.AuditLogs.Add(log);
        context.RefreshTokens.Add(token);
        context.ApiKeys.Add(apiKey);
        await context.SaveChangesAsync();

        await CreateJob(context).ExecuteAsync();

        await using var verify = await _fixture.CreateDbContextAsync();
        var storedLog = await verify.AuditLogs.AsNoTracking().SingleAsync(a => a.Id == log.Id);
        storedLog.IpAddress.Should().Be("203.0.113.42");
        storedLog.Username.Should().Be("fresh@example.com");
        storedLog.UserId.Should().Be(user.Id);
        storedLog.UserAgent.Should().Be("Mozilla/5.0");

        (await verify.RefreshTokens.AsNoTracking().SingleAsync(t => t.Id == token.Id)).CreatedByIp.Should().Be("198.51.100.7");
        (await verify.ApiKeys.AsNoTracking().SingleAsync(k => k.Id == apiKey.Id)).CreatedByIp.Should().Be("198.51.100.11");
    }

    /// <summary>
    /// La purge ne doit pas alimenter le journal d'audit : les écritures passent par
    /// <c>ExecuteUpdate</c>/<c>ExecuteDelete</c>, qui court-circuitent le change tracker.
    /// Sans cela, chaque ligne purgée générerait une nouvelle ligne de données personnelles.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_ShouldNotCreateNewAuditLogs()
    {
        await using var context = await _fixture.CreateDbContextAsync();
        var user = await SeedUserAsync(context);

        var token = NewToken(user.Id, expiresAt: DateTime.UtcNow.AddDays(-Defaults.RevokedRefreshTokenRetentionDays - 1));
        context.RefreshTokens.Add(token);
        await context.SaveChangesAsync();

        var before = await context.AuditLogs.CountAsync();

        await CreateJob(context).ExecuteAsync();

        await using var verify = await _fixture.CreateDbContextAsync();
        var after = await verify.AuditLogs.CountAsync();
        after.Should().BeLessThanOrEqualTo(before, "la purge ne doit jamais ajouter de lignes d'audit");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldProcessEveryRowWhenTheBatchSizeIsSmallerThanTheWorkload()
    {
        await using var context = await _fixture.CreateDbContextAsync();

        var old = DateTime.UtcNow.AddDays(-Defaults.IpAnonymizeAfterDays - 1);
        var logs = Enumerable.Range(0, 7)
            .Select(i => NewAuditLog(old, $"203.0.113.{i + 1}"))
            .ToList();
        context.AuditLogs.AddRange(logs);
        await context.SaveChangesAsync();

        await CreateJob(context, new DataRetentionOptions { BatchSize = 2 }).ExecuteAsync();

        await using var verify = await _fixture.CreateDbContextAsync();
        var ids = logs.Select(l => l.Id).ToList();
        var stored = await verify.AuditLogs.AsNoTracking().Where(a => ids.Contains(a.Id)).ToListAsync();
        stored.Should().HaveCount(7);
        stored.Should().OnlyContain(a => a.IpAddress == "203.0.113.0");
    }

    // ----------------------------------------------------------------------- Helpers

    private async Task<AuditLog> ReloadAsync(Guid id)
    {
        await using var verify = await _fixture.CreateDbContextAsync();
        return await verify.AuditLogs.AsNoTracking().SingleAsync(a => a.Id == id);
    }

    private static AuditLog NewAuditLog(DateTime timestamp, string? ip) => new()
    {
        Id = Guid.NewGuid(),
        EntityType = "House",
        EntityId = Guid.NewGuid().ToString(),
        Action = "Modified",
        Timestamp = timestamp,
        IpAddress = ip
    };

    private static RefreshToken NewToken(Guid userId, DateTime expiresAt, DateTime? revokedAt = null) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Token = TokenHasher.Hash(Guid.NewGuid().ToString()),
        CreatedAt = DateTime.UtcNow,
        ExpiresAt = expiresAt,
        RevokedAt = revokedAt
    };

    private static ApiKey NewApiKey(Guid userId, DateTime? revokedAt) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Name = "retention-test",
        Prefix = $"hf_{Guid.NewGuid():N}"[..12],
        KeyHash = Guid.NewGuid().ToString(),
        CreatedAt = DateTime.UtcNow,
        RevokedAt = revokedAt
    };

    private static Invitation NewInvitation(Guid houseId, Guid userId, InvitationStatus status, DateTime expiresAt) => new()
    {
        Id = Guid.NewGuid(),
        HouseId = houseId,
        CreatedByUserId = userId,
        Token = Guid.NewGuid().ToString("N"),
        Role = HouseRole.CollaboratorRO,
        Status = status,
        ExpiresAt = expiresAt,
        CreatedAt = DateTime.UtcNow
    };
}
