using FluentAssertions;
using HouseFlow.Core.Entities;
using HouseFlow.Core.Enums;
using HouseFlow.Infrastructure.Migrations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HouseFlow.IntegrationTests.Migrations;

/// <summary>
/// The SQL of the <c>NormalizeEmailCase</c> migration, run on real PostgreSQL inside a transaction
/// that is rolled back — the shared test database is left as it was.
/// </summary>
[Collection("Integration")]
public class EmailNormalizationMigrationTests
{
    private readonly IntegrationTestFixture _fixture;

    public EmailNormalizationMigrationTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    private static User NewUser(string email) => new()
    {
        Id = Guid.NewGuid(),
        Email = email,
        FirstName = "Legacy",
        LastName = "User",
        PasswordHash = "x",
        CreatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task NormalizeSql_LowerCasesAndTrimsUserAndInvitationEmails()
    {
        await using var db = await _fixture.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var user = NewUser($" Legacy.{suffix}@Example.COM");
        var house = new House { Id = Guid.NewGuid(), Name = "Maison", UserId = user.Id, CreatedAt = DateTime.UtcNow };
        var invitation = new Invitation
        {
            Id = Guid.NewGuid(),
            Token = Guid.NewGuid().ToString("N"),
            Email = $"Invitee.{suffix}@Example.COM ",
            Role = HouseRole.Tenant,
            Status = InvitationStatus.Pending,
            HouseId = house.Id,
            CreatedByUserId = user.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        db.Houses.Add(house);
        db.Invitations.Add(invitation);
        await db.SaveChangesAsync();

        await db.Database.ExecuteSqlRawAsync(NormalizeEmailCase.NormalizeSql);

        (await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id)).Email
            .Should().Be($"legacy.{suffix}@example.com");
        (await db.Invitations.AsNoTracking().SingleAsync(i => i.Id == invitation.Id)).Email
            .Should().Be($"invitee.{suffix}@example.com");

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task NormalizeSql_WithCaseDuplicateAccounts_FailsWithoutChangingAnything()
    {
        await using var db = await _fixture.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var suffix = Guid.NewGuid().ToString("N");
        db.Users.AddRange(NewUser($"twin.{suffix}@example.com"), NewUser($"Twin.{suffix}@Example.com"));
        await db.SaveChangesAsync();

        var act = () => db.Database.ExecuteSqlRawAsync(NormalizeEmailCase.NormalizeSql);

        // Loud failure (the deployment stops) rather than an arbitrary merge or deletion; the
        // message carries a count, never an address.
        var error = (await act.Should().ThrowAsync<PostgresException>()).Which;
        error.MessageText.Should().Contain("1 email address(es)").And.NotContain(suffix);

        await transaction.RollbackAsync();
    }
}
