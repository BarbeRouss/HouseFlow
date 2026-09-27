using FluentAssertions;
using HouseFlow.Application.Common;
using HouseFlow.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace HouseFlow.IntegrationTests.Gdpr;

/// <summary>
/// RGPD Art. 32/33 — kill-switch de la procédure de violation de données : révocation de
/// toutes les sessions actives. Le test reproduit l'effet exact du mode CLI
/// <c>--revoke-all-sessions</c> de <c>Program.cs</c> (mêmes requêtes, même motif), mais
/// restreint à l'utilisateur du test : la base d'intégration est partagée avec les autres
/// classes de la collection, qu'une révocation réellement globale casserait. Il vérifie
/// que rien d'actif ne survit et que les révocations antérieures ne sont pas réécrites.
/// </summary>
[Collection("Integration")]
public class MassRevocationTests
{
    private readonly IntegrationTestFixture _fixture;

    public MassRevocationTests(IntegrationTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task RevokeAllSessions_ShouldRevokeEveryActiveRefreshTokenAndApiKey()
    {
        await using var context = await _fixture.CreateDbContextAsync();

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"revoke-{Guid.NewGuid():N}@example.com",
            FirstName = "Revoke",
            LastName = "Test",
            PasswordHash = "not-a-real-hash",
            CreatedAt = DateTime.UtcNow
        };
        context.Users.Add(user);

        var alreadyRevokedAt = DateTime.UtcNow.AddDays(-2);

        var activeToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = TokenHasher.Hash(Guid.NewGuid().ToString()),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };
        var alreadyRevokedToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = TokenHasher.Hash(Guid.NewGuid().ToString()),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            RevokedAt = alreadyRevokedAt,
            ReasonRevoked = "Revoked by user"
        };

        var activeKey = new ApiKey
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Name = "revocation-test",
            Prefix = $"hf_{Guid.NewGuid():N}"[..12],
            KeyHash = Guid.NewGuid().ToString(),
            CreatedAt = DateTime.UtcNow
        };

        context.RefreshTokens.AddRange(activeToken, alreadyRevokedToken);
        context.ApiKeys.Add(activeKey);
        await context.SaveChangesAsync();

        // Same statements as the --revoke-all-sessions CLI mode.
        var revokedAt = DateTime.UtcNow;
        const string reason = "Security: mass revocation";

        var revokedTokens = await context.RefreshTokens
            .Where(t => t.RevokedAt == null && t.UserId == user.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, revokedAt)
                .SetProperty(t => t.ReasonRevoked, reason));

        var revokedKeys = await context.ApiKeys
            .Where(k => k.RevokedAt == null && k.UserId == user.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.RevokedAt, revokedAt));

        revokedTokens.Should().Be(1);
        revokedKeys.Should().Be(1);

        await using var verify = await _fixture.CreateDbContextAsync();

        var storedActive = await verify.RefreshTokens.AsNoTracking().SingleAsync(t => t.Id == activeToken.Id);
        storedActive.RevokedAt.Should().NotBeNull();
        storedActive.ReasonRevoked.Should().Be(reason);
        storedActive.IsActive.Should().BeFalse();

        // Une révocation antérieure garde sa date et son motif d'origine (traçabilité).
        var storedPrevious = await verify.RefreshTokens.AsNoTracking().SingleAsync(t => t.Id == alreadyRevokedToken.Id);
        storedPrevious.ReasonRevoked.Should().Be("Revoked by user");
        storedPrevious.RevokedAt.Should().BeCloseTo(alreadyRevokedAt, TimeSpan.FromSeconds(1));

        var storedKey = await verify.ApiKeys.AsNoTracking().SingleAsync(k => k.Id == activeKey.Id);
        storedKey.RevokedAt.Should().NotBeNull();
        storedKey.IsActive.Should().BeFalse();

        (await verify.RefreshTokens.AnyAsync(t => t.UserId == user.Id && t.RevokedAt == null)).Should().BeFalse();
        (await verify.ApiKeys.AnyAsync(k => k.UserId == user.Id && k.RevokedAt == null)).Should().BeFalse();
    }
}
