using FluentAssertions;
using HouseFlow.Application.Common;

namespace HouseFlow.UnitTests.Common;

/// <summary>
/// RGPD Art. 5(1)(e) — les durées par défaut doivent rester alignées avec celles annoncées
/// dans la politique de confidentialité et le registre des traitements. Ce test est le
/// garde-fou : toute modification d'une durée doit être délibérée et accompagnée de la mise
/// à jour des documents.
/// </summary>
public class DataRetentionOptionsTests
{
    [Fact]
    public void Defaults_ShouldMatchThePublishedRetentionPolicy()
    {
        var options = new DataRetentionOptions();

        options.IpAnonymizeAfterDays.Should().Be(30);
        options.RevokedRefreshTokenRetentionDays.Should().Be(30);
        options.RevokedApiKeyRetentionDays.Should().Be(30);
        options.AuditLogAnonymizeAfterDays.Should().Be(365);
        options.AuditLogDeleteAfterDays.Should().Be(1095); // 3 ans
        options.SoftDeletedRetentionDays.Should().Be(30);
        options.ExpiredInvitationRetentionDays.Should().Be(30);
    }

    [Fact]
    public void Defaults_ShouldUseBatchedDailyExecution()
    {
        var options = new DataRetentionOptions();

        options.BatchSize.Should().Be(500);
        options.Cron.Should().Be("0 3 * * *"); // tous les jours à 03:00 UTC
    }

    [Fact]
    public void SectionName_ShouldMatchAppSettingsSection()
    {
        DataRetentionOptions.SectionName.Should().Be("DataRetention");
    }

    [Fact]
    public void AuditLogDeletion_ShouldHappenAfterAnonymization()
    {
        var options = new DataRetentionOptions();

        // Un journal doit d'abord être anonymisé, puis supprimé — jamais l'inverse.
        options.AuditLogDeleteAfterDays.Should().BeGreaterThan(options.AuditLogAnonymizeAfterDays);
        options.AuditLogAnonymizeAfterDays.Should().BeGreaterThan(options.IpAnonymizeAfterDays);
    }
}
