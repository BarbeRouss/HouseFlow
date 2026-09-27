using FluentAssertions;
using HouseFlow.Application.Common;
using HouseFlow.Application.Services;
using HouseFlow.Core.Entities;
using HouseFlow.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace HouseFlow.UnitTests.Services;

public class ConsentServiceTests
{
    private readonly DbContextOptions<HouseFlowDbContext> _dbContextOptions;
    private readonly Mock<ILogger<ConsentService>> _mockLogger = new();

    public ConsentServiceTests()
    {
        _dbContextOptions = new DbContextOptionsBuilder<HouseFlowDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
    }

    private static async Task<User> SeedUserAsync(HouseFlowDbContext context, DateTime? consentGivenAt = null, string? version = null)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"user-{Guid.NewGuid()}@example.com",
            PasswordHash = "hash",
            FirstName = "Test",
            LastName = "User",
            CreatedAt = DateTime.UtcNow,
            ConsentGivenAt = consentGivenAt,
            ConsentPolicyVersion = version
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task GetConsentStatusAsync_ForLegacyUser_ReportsConsentRequired()
    {
        using var context = new HouseFlowDbContext(_dbContextOptions);
        var user = await SeedUserAsync(context);
        var service = new ConsentService(context, _mockLogger.Object);

        var status = await service.GetConsentStatusAsync(user.Id);

        status.ConsentGivenAt.Should().BeNull();
        status.ConsentPolicyVersion.Should().BeNull();
        status.ConsentRequired.Should().BeTrue();
        status.CurrentPolicyVersion.Should().Be(GdprPolicy.CurrentPolicyVersion);
    }

    [Fact]
    public async Task GetConsentStatusAsync_ForUpToDateUser_ReportsNoConsentRequired()
    {
        using var context = new HouseFlowDbContext(_dbContextOptions);
        var user = await SeedUserAsync(context, DateTime.UtcNow, GdprPolicy.CurrentPolicyVersion);
        var service = new ConsentService(context, _mockLogger.Object);

        var status = await service.GetConsentStatusAsync(user.Id);

        status.ConsentRequired.Should().BeFalse();
        status.ConsentPolicyVersion.Should().Be(GdprPolicy.CurrentPolicyVersion);
    }

    [Fact]
    public async Task RecordConsentAsync_WithCurrentVersion_StoresTimestampAndVersion()
    {
        using var context = new HouseFlowDbContext(_dbContextOptions);
        var user = await SeedUserAsync(context);
        var service = new ConsentService(context, _mockLogger.Object);
        var before = DateTime.UtcNow;

        var status = await service.RecordConsentAsync(user.Id, true, GdprPolicy.CurrentPolicyVersion, "203.0.113.7");

        status.ConsentRequired.Should().BeFalse();
        status.ConsentGivenAt.Should().NotBeNull();
        status.CurrentPolicyVersion.Should().Be(GdprPolicy.CurrentPolicyVersion);

        var stored = await context.Users.FirstAsync(u => u.Id == user.Id);
        stored.ConsentGivenAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(DateTime.UtcNow);
        stored.ConsentPolicyVersion.Should().Be(GdprPolicy.CurrentPolicyVersion);
        stored.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RecordConsentAsync_WritesAnAuditLogWithTheIpAddress()
    {
        using var context = new HouseFlowDbContext(_dbContextOptions);
        var user = await SeedUserAsync(context);
        var service = new ConsentService(context, _mockLogger.Object);

        await service.RecordConsentAsync(user.Id, true, GdprPolicy.CurrentPolicyVersion, "203.0.113.9");

        // Preuve d'accountability (Art. 5(2)) : qui a accepté, quand, depuis quelle IP.
        var audit = await context.AuditLogs
            .Where(a => a.EntityType == "User" && a.Action == "Modified")
            .OrderByDescending(a => a.Timestamp)
            .FirstAsync();

        audit.UserId.Should().Be(user.Id);
        audit.IpAddress.Should().Be("203.0.113.9");
        audit.NewValues.Should().Contain(GdprPolicy.CurrentPolicyVersion);
    }

    [Fact]
    public async Task RecordConsentAsync_WhenNotAccepted_ThrowsAndChangesNothing()
    {
        using var context = new HouseFlowDbContext(_dbContextOptions);
        var user = await SeedUserAsync(context);
        var service = new ConsentService(context, _mockLogger.Object);

        var act = async () => await service.RecordConsentAsync(user.Id, false, GdprPolicy.CurrentPolicyVersion);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*accept the terms of service*");
        (await context.Users.FirstAsync(u => u.Id == user.Id)).ConsentGivenAt.Should().BeNull();
    }

    [Fact]
    public async Task RecordConsentAsync_WithUnknownPolicyVersion_ThrowsAndChangesNothing()
    {
        using var context = new HouseFlowDbContext(_dbContextOptions);
        var user = await SeedUserAsync(context);
        var service = new ConsentService(context, _mockLogger.Object);

        var act = async () => await service.RecordConsentAsync(user.Id, true, "1900-01-01");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Unknown policy version*");
        (await context.Users.FirstAsync(u => u.Id == user.Id)).ConsentGivenAt.Should().BeNull();
    }

    [Fact]
    public async Task RecordConsentAsync_ForUnknownUser_ThrowsKeyNotFound()
    {
        using var context = new HouseFlowDbContext(_dbContextOptions);
        var service = new ConsentService(context, _mockLogger.Object);

        var act = async () => await service.RecordConsentAsync(Guid.NewGuid(), true, GdprPolicy.CurrentPolicyVersion);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
