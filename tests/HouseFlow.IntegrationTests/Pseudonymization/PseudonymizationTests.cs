using FluentAssertions;
using HouseFlow.Core.Entities;
using HouseFlow.Core.Enums;
using HouseFlow.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HouseFlow.IntegrationTests.Pseudonymization;

/// <summary>
/// dbtools/pseudonymize.sql and dbtools/verify.sql — what `dbtools dump` runs on a copy of the
/// production database before anything leaves it — against the real schema: a database of its
/// own, created by the EF migrations and seeded through the DbContext, on the test server.
/// </summary>
[Collection("Integration")]
public class PseudonymizationTests
{
    private const string DatabaseName = "houseflow_pseudonymization";
    private const string PreservedEmails = "julienrousselle@outlook.be, demo@demo.com";
    private static readonly string NeutralizedHash = "$2a$11$" + new string('A', 53);

    // Every string column of the model is either handled by pseudonymize.sql or declared
    // non-personal here: a column added later fails Model_EveryStringColumn_IsClassified
    // until someone decides which it is.
    private static readonly HashSet<string> PseudonymizedColumns =
    [
        "Users.Email", "Users.PasswordHash", "Users.FirstName", "Users.LastName",
        "RefreshTokens.Token", "RefreshTokens.CreatedByIp", "RefreshTokens.RevokedByIp",
        "RefreshTokens.ReplacedByToken", "RefreshTokens.ReasonRevoked",
        "ApiKeys.Name", "ApiKeys.Prefix", "ApiKeys.KeyHash", "ApiKeys.CreatedByIp",
        "AuditLogs.Username", "AuditLogs.IpAddress", "AuditLogs.UserAgent",
        "AuditLogs.OldValues", "AuditLogs.NewValues", "AuditLogs.AdditionalData",
        "Invitations.Token",
        "Houses.Name", "Houses.Address", "Houses.ZipCode", "Houses.City",
        "MaintenanceInstances.Provider", "MaintenanceInstances.Notes",
    ];

    private static readonly HashSet<string> NonPersonalColumns =
    [
        "Users.Theme", "Users.Language",
        "ApiKeys.Scope",
        "AuditLogs.EntityType", "AuditLogs.EntityId", "AuditLogs.Action", "AuditLogs.ChangedProperties",
        "Invitations.Role", "Invitations.Status",
        "HouseMembers.Role",
        "Houses.Country",
        "Devices.Name", "Devices.Type", "Devices.Brand", "Devices.Model",
        "MaintenanceTypes.Name",
    ];

    private static readonly Guid MaintainerId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid MaintainerHouseId = Guid.NewGuid();
    private static readonly Guid OtherHouseId = Guid.NewGuid();

    private readonly IntegrationTestFixture _fixture;

    public PseudonymizationTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Model_EveryStringColumn_IsClassified()
    {
        using var context = new HouseFlowDbContext(
            new DbContextOptionsBuilder<HouseFlowDbContext>().UseNpgsql("Host=unused").Options);

        var stringColumns = context.Model.GetEntityTypes()
            .SelectMany(entity => entity.GetProperties().Select(property => (entity, property)))
            .Where(x => (x.property.GetTypeMapping().Converter?.ProviderClrType ?? x.property.ClrType) == typeof(string))
            .Select(x => $"{x.entity.GetTableName()}.{x.property.GetColumnName()}")
            .ToHashSet();

        stringColumns.Except(PseudonymizedColumns).Except(NonPersonalColumns).Should().BeEmpty(
            "a new text column must be pseudonymized in dbtools/pseudonymize.sql (and checked in verify.sql), or declared non-personal in this test");
        PseudonymizedColumns.Concat(NonPersonalColumns).Except(stringColumns).Should().BeEmpty(
            "a classified column that no longer exists is a stale entry");
    }

    [Fact]
    public async Task Verify_OnUnpseudonymizedData_ReportsEveryCheck()
    {
        await using var connection = await CreateSeededDatabaseAsync();

        var violations = await VerifyAsync(connection);

        violations.Should().BeEquivalentTo(
        [
            "ApiKeys", "AuditLogs", "Houses.Address", "Houses.City", "Houses.Name", "Houses.ZipCode",
            "Invitations.Token", "MaintenanceInstances.Notes", "MaintenanceInstances.Provider",
            "RefreshTokens", "Users.Email", "Users.FirstName", "Users.LastName", "Users.PasswordHash",
        ], "a check that cannot fail proves nothing");
    }

    [Fact]
    public async Task Pseudonymize_LeavesNoPersonalDataOutsideTheAllowList()
    {
        await using var connection = await CreateSeededDatabaseAsync();

        await PseudonymizeAsync(connection);

        (await VerifyAsync(connection)).Should().BeEmpty();

        var other = await SingleRowAsync(connection,
            """SELECT "Email", "FirstName", "LastName", "PasswordHash" FROM "Users" WHERE "Id" = @id""", OtherUserId);
        other[0].Should().Be($"user-{OtherUserId:N}@pseudonymise.invalid");
        other[1].Should().NotBe("Marie");
        other[2].Should().NotContain("Dupont");
        other[3].Should().Be(NeutralizedHash);

        var otherHouse = await SingleRowAsync(connection,
            """SELECT "Name", "Address", "ZipCode", "City", "Country" FROM "Houses" WHERE "Id" = @id""", OtherHouseId);
        otherHouse.Should().Equal($"Maison {OtherHouseId:N}"[..15], "Adresse pseudonymisée", "0000", "Ville pseudonymisée", "FR");

        (await ScalarAsync(connection, """SELECT count(*) FROM "RefreshTokens" """)).Should().Be(0L);
        (await ScalarAsync(connection, """SELECT count(*) FROM "AuditLogs" """)).Should().BeGreaterThan(0L,
            "audit rows are kept, only their personal columns are emptied");
        (await ScalarAsync(connection,
            """SELECT count(*) FROM "AuditLogs" WHERE coalesce("OldValues", "NewValues", "Username", "IpAddress", "UserAgent") IS NOT NULL""")).Should().Be(0L);
    }

    [Fact]
    public async Task Pseudonymize_KeepsPreservedAccountsAndTheirHousesIntact()
    {
        await using var connection = await CreateSeededDatabaseAsync();

        await PseudonymizeAsync(connection);

        // Matched case-insensitively: the allow-list is lowercase, the stored address is not.
        var maintainer = await SingleRowAsync(connection,
            """SELECT "Email", "FirstName", "LastName", "PasswordHash" FROM "Users" WHERE "Id" = @id""", MaintainerId);
        maintainer.Should().Equal("JulienRousselle@outlook.be", "Julien", "Rousselle", "maintainer-real-hash");

        var house = await SingleRowAsync(connection,
            """SELECT "Name", "Address", "ZipCode", "City" FROM "Houses" WHERE "Id" = @id""", MaintainerHouseId);
        house.Should().Equal("Chez Julien", "1 rue du Mainteneur", "1000", "Bruxelles");

        var maintenance = await SingleRowAsync(connection,
            """
            SELECT mi."Provider", mi."Notes" FROM "MaintenanceInstances" mi
            JOIN "MaintenanceTypes" mt ON mt."Id" = mi."MaintenanceTypeId"
            JOIN "Devices" d ON d."Id" = mt."DeviceId"
            WHERE d."HouseId" = @id
            """, MaintainerHouseId);
        maintenance.Should().Equal("Plomberie du coin", "Filtre changé");

        (await ScalarAsync(connection, """SELECT count(*) FROM "ApiKeys" WHERE "UserId" = @id""", MaintainerId)).Should().Be(1L);
        (await ScalarAsync(connection, """SELECT count(*) FROM "ApiKeys" WHERE "UserId" = @id""", OtherUserId)).Should().Be(0L);
    }

    [Fact]
    public void NeutralizedHash_IsRejectedByBCrypt_WithoutThrowing()
    {
        // A malformed hash would make the login endpoint throw instead of answering 401.
        var verify = () => BCrypt.Net.BCrypt.Verify("Demo@2026!", NeutralizedHash);

        verify.Should().NotThrow().Which.Should().BeFalse();
    }

    private async Task<NpgsqlConnection> CreateSeededDatabaseAsync()
    {
        var server = new NpgsqlConnectionStringBuilder(await _fixture.GetDatabaseConnectionStringAsync());
        var admin = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = "postgres", Pooling = false };
        await using (var adminConnection = new NpgsqlConnection(admin.ConnectionString))
        {
            await adminConnection.OpenAsync();
            await using var reset = new NpgsqlCommand(
                $"DROP DATABASE IF EXISTS {DatabaseName} WITH (FORCE); CREATE DATABASE {DatabaseName};", adminConnection);
            await reset.ExecuteNonQueryAsync();
        }

        var target = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = DatabaseName, Pooling = false };
        var options = new DbContextOptionsBuilder<HouseFlowDbContext>().UseNpgsql(target.ConnectionString).Options;
        await using (var context = new HouseFlowDbContext(options))
        {
            await context.Database.MigrateAsync();
            Seed(context);
            await context.SaveChangesAsync();
        }

        var connection = new NpgsqlConnection(target.ConnectionString);
        await connection.OpenAsync();
        await using var setAllowList = new NpgsqlCommand(
            "SELECT set_config('dbtools.preserved_emails', @emails, false)", connection);
        setAllowList.Parameters.AddWithValue("emails", PreservedEmails);
        await setAllowList.ExecuteScalarAsync();
        return connection;
    }

    private static void Seed(HouseFlowDbContext context)
    {
        var now = DateTime.UtcNow;
        User UserOf(Guid id, string email, string first, string last, string hash) =>
            new() { Id = id, Email = email, FirstName = first, LastName = last, PasswordHash = hash, CreatedAt = now };

        context.Users.AddRange(
            UserOf(MaintainerId, "JulienRousselle@outlook.be", "Julien", "Rousselle", "maintainer-real-hash"),
            UserOf(OtherUserId, "marie.dupont@gmail.com", "Marie", "Dupont", "marie-real-hash"));

        context.Houses.AddRange(
            new House
            {
                Id = MaintainerHouseId, UserId = MaintainerId, Name = "Chez Julien", CreatedAt = now,
                Address = "1 rue du Mainteneur", ZipCode = "1000", City = "Bruxelles", Country = "BE",
            },
            new House
            {
                Id = OtherHouseId, UserId = OtherUserId, Name = "Maison de Marie Dupont", CreatedAt = now,
                Address = "12 avenue des Lilas", ZipCode = "75011", City = "Paris", Country = "FR",
            });

        foreach (var (houseId, provider, notes) in new[]
                 {
                     (MaintainerHouseId, "Plomberie du coin", "Filtre changé"),
                     (OtherHouseId, "Pierre Durand, 0470 12 34 56", "Code du portail 4521, appeler Marie"),
                 })
        {
            var device = new Device { Id = Guid.NewGuid(), HouseId = houseId, Name = "Chaudière", Type = "Heating", CreatedAt = now };
            var type = new MaintenanceType { Id = Guid.NewGuid(), DeviceId = device.Id, Name = "Entretien annuel", Periodicity = Periodicity.Annual, CreatedAt = now };
            context.Devices.Add(device);
            context.MaintenanceTypes.Add(type);
            context.MaintenanceInstances.Add(new MaintenanceInstance
            {
                Id = Guid.NewGuid(), MaintenanceTypeId = type.Id, Date = now, Provider = provider, Notes = notes, CreatedAt = now,
            });
        }

        context.Invitations.Add(new Invitation
        {
            Id = Guid.NewGuid(), HouseId = OtherHouseId, CreatedByUserId = OtherUserId, Token = "real-invitation-token",
            Role = HouseRole.CollaboratorRW, ExpiresAt = now.AddDays(7), CreatedAt = now,
        });

        foreach (var userId in new[] { MaintainerId, OtherUserId })
        {
            context.RefreshTokens.Add(new RefreshToken
            {
                Id = Guid.NewGuid(), UserId = userId, Token = $"real-refresh-{userId:N}", ExpiresAt = now.AddDays(1),
                CreatedAt = now, CreatedByIp = "81.2.3.4",
            });
            context.ApiKeys.Add(new ApiKey
            {
                Id = Guid.NewGuid(), UserId = userId, Name = "Ma clé", Prefix = $"hf_{userId:N}"[..10],
                KeyHash = $"{userId:N}", CreatedAt = now, CreatedByIp = "81.2.3.4",
            });
        }

        context.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(), EntityType = "User", EntityId = OtherUserId.ToString(), Action = "Modified",
            UserId = OtherUserId, Username = "marie.dupont@gmail.com", Timestamp = now,
            OldValues = """{"LastName":"Dupond"}""", NewValues = """{"LastName":"Dupont"}""",
            IpAddress = "81.2.3.4", UserAgent = "Mozilla/5.0", AdditionalData = """{"reason":"typo"}""",
        });
    }

    private static async Task PseudonymizeAsync(NpgsqlConnection connection)
    {
        // `dbtools dump` runs it the same way: one transaction (psql -1).
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand(ReadSql("pseudonymize.sql"), connection, transaction))
        {
            await command.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }

    private static async Task<List<string>> VerifyAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(ReadSql("verify.sql"), connection);
        await using var reader = await command.ExecuteReaderAsync();
        var violations = new List<string>();
        while (await reader.ReadAsync())
        {
            violations.Add(reader.GetString(0));
        }
        return violations;
    }

    private static async Task<string?[]> SingleRowAsync(NpgsqlConnection connection, string sql, Guid id)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        var row = new string?[reader.FieldCount];
        for (var i = 0; i < row.Length; i++)
        {
            row[i] = reader.IsDBNull(i) ? null : reader.GetString(i);
        }
        (await reader.ReadAsync()).Should().BeFalse("exactly one row was expected");
        return row;
    }

    private static async Task<long> ScalarAsync(NpgsqlConnection connection, string sql, Guid? id = null)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        if (id is not null)
        {
            command.Parameters.AddWithValue("id", id.Value);
        }
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static string ReadSql(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "dbtools", name));
}
