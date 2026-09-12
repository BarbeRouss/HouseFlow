using System.Text.Json;
using FluentAssertions;
using HouseFlow.Application.Common;
using HouseFlow.Application.Services;
using HouseFlow.Core.Entities;
using HouseFlow.Core.Enums;
using HouseFlow.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using BCryptNet = BCrypt.Net.BCrypt;

namespace HouseFlow.UnitTests.Services;

/// <summary>
/// Droits RGPD en self-service : accès (Art. 15), rectification (Art. 16),
/// effacement (Art. 17) et portabilité (Art. 20).
/// </summary>
public class UserAccountServiceTests
{
    private const string Password = "CorrectHorseBattery1";

    private readonly DbContextOptions<HouseFlowDbContext> _options = new DbContextOptionsBuilder<HouseFlowDbContext>()
        .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
        .Options;

    private static UserAccountService CreateService(HouseFlowDbContext context) =>
        new(context, new MaintenanceCalculatorService(), NullLogger<UserAccountService>.Instance);

    private static User NewUser(string email = "owner@example.com") => new()
    {
        Id = Guid.NewGuid(),
        Email = email,
        FirstName = "Alice",
        LastName = "Martin",
        PasswordHash = BCryptNet.HashPassword(Password),
        CreatedAt = DateTime.UtcNow.AddDays(-30),
        ConsentGivenAt = DateTime.UtcNow.AddDays(-30),
        ConsentPolicyVersion = GdprPolicy.CurrentPolicyVersion
    };

    private static House NewHouse(Guid ownerId, string name = "Maison") => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Address = "1 rue des Lilas",
        ZipCode = "75001",
        City = "Paris",
        Country = "FR",
        UserId = ownerId,
        CreatedAt = DateTime.UtcNow.AddDays(-20)
    };

    private static HouseMember NewMember(Guid houseId, Guid userId, HouseRole role, DateTime? createdAt = null) => new()
    {
        Id = Guid.NewGuid(),
        HouseId = houseId,
        UserId = userId,
        Role = role,
        CanLogMaintenance = true,
        CanViewCosts = role != HouseRole.Tenant,
        CreatedAt = createdAt ?? DateTime.UtcNow.AddDays(-10)
    };

    // ====================================================================
    // Art. 16 — rectification
    // ====================================================================

    [Fact]
    public async Task UpdateProfileAsync_TrimsValuesAndUpdatesUser()
    {
        using var context = new HouseFlowDbContext(_options);
        var user = NewUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        var profile = await service.UpdateProfileAsync(user.Id,
            new UpdateProfileRequestDto(email: "  new@example.com ", firstName: " Bob ", lastName: " Dupont "));

        profile.FirstName.Should().Be("Bob");
        profile.LastName.Should().Be("Dupont");
        profile.Email.Should().Be("new@example.com");

        var reloaded = await context.Users.FirstAsync(u => u.Id == user.Id);
        reloaded.Email.Should().Be("new@example.com");
        reloaded.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateProfileAsync_WithEmailOfAnotherAccount_Throws()
    {
        using var context = new HouseFlowDbContext(_options);
        var user = NewUser("me@example.com");
        var other = NewUser("taken@example.com");
        context.Users.AddRange(user, other);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        var act = () => service.UpdateProfileAsync(user.Id,
            new UpdateProfileRequestDto(email: "taken@example.com", firstName: "Bob", lastName: "Dupont"));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already used*");
    }

    [Fact]
    public async Task GetProfileAsync_WithOutdatedPolicyVersion_RequiresConsent()
    {
        using var context = new HouseFlowDbContext(_options);
        var user = NewUser();
        user.ConsentGivenAt = null;
        user.ConsentPolicyVersion = null;
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var profile = await CreateService(context).GetProfileAsync(user.Id);

        profile.ConsentRequired.Should().BeTrue();
        profile.ConsentGivenAt.Should().BeNull();
    }

    // ====================================================================
    // Art. 17 — effacement
    // ====================================================================

    [Fact]
    public async Task DeleteAccountAsync_WithWrongPassword_ThrowsAndDeletesNothing()
    {
        using var context = new HouseFlowDbContext(_options);
        var user = NewUser();
        var house = NewHouse(user.Id);
        context.Users.Add(user);
        context.Houses.Add(house);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        var act = () => service.DeleteAccountAsync(user.Id, "WrongPassword123", "203.0.113.7");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Invalid password");

        (await context.Users.CountAsync()).Should().Be(1);
        (await context.Houses.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task DeleteAccountAsync_PurgesAccountDataAndAnonymizesAuditTrail()
    {
        using var context = new HouseFlowDbContext(_options);
        var user = NewUser();
        var house = NewHouse(user.Id);

        // Comme en production : le DbContext journalise chaque écriture au nom de
        // l'utilisateur courant. Ces entrées doivent elles aussi finir anonymisées.
        context.SetAuditContext(user.Id, user.Email, "203.0.113.7", "Mozilla/5.0");

        var device = new Device { Id = Guid.NewGuid(), Name = "Chaudière", Type = "boiler", HouseId = house.Id, CreatedAt = DateTime.UtcNow };
        var type = new MaintenanceType { Id = Guid.NewGuid(), Name = "Révision", Periodicity = Periodicity.Annual, DeviceId = device.Id, CreatedAt = DateTime.UtcNow };
        var instance = new MaintenanceInstance { Id = Guid.NewGuid(), Date = DateTime.UtcNow.AddDays(-5), Cost = 120m, Provider = "Plombier", MaintenanceTypeId = type.Id, CreatedAt = DateTime.UtcNow };

        context.Users.Add(user);
        context.Houses.Add(house);
        context.Devices.Add(device);
        context.MaintenanceTypes.Add(type);
        context.MaintenanceInstances.Add(instance);
        context.HouseMembers.Add(NewMember(house.Id, user.Id, HouseRole.Owner));
        context.RefreshTokens.Add(new RefreshToken { Id = Guid.NewGuid(), UserId = user.Id, Token = "secret-token", ExpiresAt = DateTime.UtcNow.AddDays(7), CreatedAt = DateTime.UtcNow, CreatedByIp = "203.0.113.7" });
        context.ApiKeys.Add(new ApiKey { Id = Guid.NewGuid(), UserId = user.Id, Name = "CI", Prefix = "hf_abc", KeyHash = "hashed", CreatedAt = DateTime.UtcNow });
        context.Invitations.Add(new Invitation { Id = Guid.NewGuid(), Token = "invite-token", Role = HouseRole.CollaboratorRW, HouseId = house.Id, CreatedByUserId = user.Id, ExpiresAt = DateTime.UtcNow.AddDays(7), CreatedAt = DateTime.UtcNow });
        context.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            EntityType = "House",
            EntityId = house.Id.ToString(),
            Action = "Update",
            UserId = user.Id,
            Username = user.Email,
            IpAddress = "203.0.113.7",
            UserAgent = "Mozilla/5.0",
            OldValues = "{\"Name\":\"Ancien\"}",
            NewValues = "{\"Name\":\"Maison\"}",
            ChangedProperties = "[\"Name\"]",
            Timestamp = DateTime.UtcNow.AddDays(-1)
        });
        await context.SaveChangesAsync();

        await CreateService(context).DeleteAccountAsync(user.Id, Password, "203.0.113.7");

        (await context.Users.CountAsync()).Should().Be(0);
        (await context.Houses.CountAsync()).Should().Be(0);
        (await context.Devices.CountAsync()).Should().Be(0);
        (await context.MaintenanceTypes.CountAsync()).Should().Be(0);
        (await context.MaintenanceInstances.CountAsync()).Should().Be(0);
        (await context.HouseMembers.CountAsync()).Should().Be(0);
        (await context.RefreshTokens.CountAsync()).Should().Be(0);
        (await context.ApiKeys.CountAsync()).Should().Be(0);
        (await context.Invitations.CountAsync()).Should().Be(0);

        var logs = await context.AuditLogs.AsNoTracking().ToListAsync();
        logs.Should().NotBeEmpty();
        logs.Should().OnlyContain(l => l.UserId == null);
        logs.Should().OnlyContain(l => l.Username == GdprPolicy.DeletedUserName);
        logs.Should().OnlyContain(l => l.IpAddress == null && l.UserAgent == null);
        logs.Should().OnlyContain(l => l.OldValues == null && l.NewValues == null && l.ChangedProperties == null);

        logs.Should().ContainSingle(l => l.Action == "AccountDeleted" && l.EntityType == "User" && l.EntityId == "deleted");
        logs.Should().NotContain(l => l.EntityType == "User" && l.EntityId == user.Id.ToString(), "the deleted account's UUID must no longer individualise audit rows");
    }

    [Fact]
    public async Task DeleteAccountAsync_TransfersOwnedHouseToOldestCollaboratorRw()
    {
        using var context = new HouseFlowDbContext(_options);
        var owner = NewUser("owner@example.com");
        var readOnly = NewUser("ro@example.com");
        var recentRw = NewUser("rw-recent@example.com");
        var oldestRw = NewUser("rw-oldest@example.com");
        var house = NewHouse(owner.Id);

        context.Users.AddRange(owner, readOnly, recentRw, oldestRw);
        context.Houses.Add(house);
        context.HouseMembers.AddRange(
            NewMember(house.Id, owner.Id, HouseRole.Owner),
            NewMember(house.Id, readOnly.Id, HouseRole.CollaboratorRO, DateTime.UtcNow.AddDays(-15)),
            NewMember(house.Id, oldestRw.Id, HouseRole.CollaboratorRW, DateTime.UtcNow.AddDays(-12)),
            NewMember(house.Id, recentRw.Id, HouseRole.CollaboratorRW, DateTime.UtcNow.AddDays(-2)));
        await context.SaveChangesAsync();

        await CreateService(context).DeleteAccountAsync(owner.Id, Password);

        var reloaded = await context.Houses.AsNoTracking().FirstAsync(h => h.Id == house.Id);
        reloaded.UserId.Should().Be(oldestRw.Id);

        var members = await context.HouseMembers.AsNoTracking().Where(m => m.HouseId == house.Id).ToListAsync();
        members.Should().HaveCount(3);
        members.Should().NotContain(m => m.UserId == owner.Id);
        members.Single(m => m.UserId == oldestRw.Id).Role.Should().Be(HouseRole.Owner);
    }

    [Fact]
    public async Task DeleteAccountAsync_HouseWithOnlyTenant_DeletesTheHouse()
    {
        using var context = new HouseFlowDbContext(_options);
        var owner = NewUser("owner@example.com");
        var tenant = NewUser("tenant@example.com");
        var house = NewHouse(owner.Id);

        context.Users.AddRange(owner, tenant);
        context.Houses.Add(house);
        context.HouseMembers.AddRange(
            NewMember(house.Id, owner.Id, HouseRole.Owner),
            NewMember(house.Id, tenant.Id, HouseRole.Tenant));
        await context.SaveChangesAsync();

        await CreateService(context).DeleteAccountAsync(owner.Id, Password);

        (await context.Houses.CountAsync()).Should().Be(0);
        (await context.HouseMembers.CountAsync()).Should().Be(0);
        (await context.Users.CountAsync()).Should().Be(1); // le locataire survit
    }

    [Fact]
    public async Task DeleteAccountAsync_MembershipInSomeoneElsesHouse_RemovesMemberOnly()
    {
        using var context = new HouseFlowDbContext(_options);
        var owner = NewUser("owner@example.com");
        var guest = NewUser("guest@example.com");
        var house = NewHouse(owner.Id);

        context.Users.AddRange(owner, guest);
        context.Houses.Add(house);
        context.HouseMembers.AddRange(
            NewMember(house.Id, owner.Id, HouseRole.Owner),
            NewMember(house.Id, guest.Id, HouseRole.CollaboratorRW));
        await context.SaveChangesAsync();

        await CreateService(context).DeleteAccountAsync(guest.Id, Password);

        var reloaded = await context.Houses.AsNoTracking().FirstAsync(h => h.Id == house.Id);
        reloaded.UserId.Should().Be(owner.Id);

        var members = await context.HouseMembers.AsNoTracking().ToListAsync();
        members.Should().ContainSingle().Which.UserId.Should().Be(owner.Id);
    }

    [Fact]
    public async Task DeleteAccountAsync_ClearsAcceptedInvitationLinkWithoutDeletingIt()
    {
        using var context = new HouseFlowDbContext(_options);
        var owner = NewUser("owner@example.com");
        var guest = NewUser("guest@example.com");
        var house = NewHouse(owner.Id);
        var invitation = new Invitation
        {
            Id = Guid.NewGuid(),
            Token = "invite-token",
            Role = HouseRole.CollaboratorRW,
            Status = InvitationStatus.Accepted,
            HouseId = house.Id,
            CreatedByUserId = owner.Id,
            AcceptedByUserId = guest.Id,
            AcceptedAt = DateTime.UtcNow.AddDays(-1),
            ExpiresAt = DateTime.UtcNow.AddDays(6),
            CreatedAt = DateTime.UtcNow.AddDays(-2)
        };

        context.Users.AddRange(owner, guest);
        context.Houses.Add(house);
        context.HouseMembers.AddRange(
            NewMember(house.Id, owner.Id, HouseRole.Owner),
            NewMember(house.Id, guest.Id, HouseRole.CollaboratorRW));
        context.Invitations.Add(invitation);
        await context.SaveChangesAsync();

        await CreateService(context).DeleteAccountAsync(guest.Id, Password);

        var reloaded = await context.Invitations.AsNoTracking().FirstAsync(i => i.Id == invitation.Id);
        reloaded.AcceptedByUserId.Should().BeNull();
    }

    // ====================================================================
    // Art. 15 + 20 — accès et portabilité
    // ====================================================================

    [Fact]
    public async Task ExportDataAsync_ReturnsEverySection()
    {
        using var context = new HouseFlowDbContext(_options);
        var user = NewUser();
        var otherOwner = NewUser("other@example.com");
        var house = NewHouse(user.Id, "Ma maison");
        var sharedHouse = NewHouse(otherOwner.Id, "Maison partagée");
        var device = new Device { Id = Guid.NewGuid(), Name = "Chaudière", Type = "boiler", Brand = "Vaillant", Model = "X1", InstallDate = DateTime.UtcNow.AddYears(-2), HouseId = house.Id, CreatedAt = DateTime.UtcNow.AddDays(-19) };
        var type = new MaintenanceType { Id = Guid.NewGuid(), Name = "Révision", Periodicity = Periodicity.Custom, CustomDays = 180, DeviceId = device.Id, CreatedAt = DateTime.UtcNow.AddDays(-18) };
        var instance = new MaintenanceInstance { Id = Guid.NewGuid(), Date = DateTime.UtcNow.AddDays(-10), Cost = 99.90m, Provider = "Plombier Durand", Notes = "RAS", MaintenanceTypeId = type.Id, CreatedAt = DateTime.UtcNow.AddDays(-10) };

        context.Users.AddRange(user, otherOwner);
        context.Houses.AddRange(house, sharedHouse);
        context.Devices.Add(device);
        context.MaintenanceTypes.Add(type);
        context.MaintenanceInstances.Add(instance);
        context.HouseMembers.AddRange(
            NewMember(house.Id, user.Id, HouseRole.Owner),
            NewMember(sharedHouse.Id, otherOwner.Id, HouseRole.Owner),
            NewMember(sharedHouse.Id, user.Id, HouseRole.CollaboratorRO));
        context.Invitations.AddRange(
            new Invitation { Id = Guid.NewGuid(), Token = "sent-token", Role = HouseRole.Tenant, HouseId = house.Id, CreatedByUserId = user.Id, ExpiresAt = DateTime.UtcNow.AddDays(7), CreatedAt = DateTime.UtcNow.AddDays(-3) },
            new Invitation { Id = Guid.NewGuid(), Token = "received-token", Role = HouseRole.CollaboratorRO, Status = InvitationStatus.Accepted, HouseId = sharedHouse.Id, CreatedByUserId = otherOwner.Id, AcceptedByUserId = user.Id, AcceptedAt = DateTime.UtcNow.AddDays(-4), ExpiresAt = DateTime.UtcNow.AddDays(3), CreatedAt = DateTime.UtcNow.AddDays(-5) });
        context.ApiKeys.Add(new ApiKey { Id = Guid.NewGuid(), UserId = user.Id, Name = "CI", Prefix = "hf_abc", KeyHash = "super-secret-hash", Scope = ApiKeyScope.ReadOnly, CreatedAt = DateTime.UtcNow.AddDays(-6) });
        context.RefreshTokens.Add(new RefreshToken { Id = Guid.NewGuid(), UserId = user.Id, Token = "super-secret-token", ExpiresAt = DateTime.UtcNow.AddDays(7), CreatedAt = DateTime.UtcNow.AddDays(-1), CreatedByIp = "203.0.113.7" });
        context.AuditLogs.Add(new AuditLog { Id = Guid.NewGuid(), EntityType = "House", EntityId = house.Id.ToString(), Action = "Update", UserId = user.Id, Username = user.Email, IpAddress = "203.0.113.7", UserAgent = "Mozilla/5.0", Timestamp = DateTime.UtcNow.AddDays(-1) });
        await context.SaveChangesAsync();

        var export = await CreateService(context).ExportDataAsync(user.Id, "203.0.113.7");

        export.FormatVersion.Should().Be("1.0");
        export.Profile.Email.Should().Be(user.Email);
        export.Preferences.Language.Should().Be("fr");
        export.Consent.ConsentPolicyVersion.Should().Be(GdprPolicy.CurrentPolicyVersion);

        var exportedHouse = export.Houses.Should().ContainSingle().Subject;
        exportedHouse.Name.Should().Be("Ma maison");
        var exportedDevice = exportedHouse.Devices.Should().ContainSingle().Subject;
        exportedDevice.Brand.Should().Be("Vaillant");
        var exportedType = exportedDevice.MaintenanceTypes.Should().ContainSingle().Subject;
        exportedType.Periodicity.Should().Be("Custom");
        exportedType.CustomDays.Should().Be(180);
        exportedType.Status.Should().NotBeNullOrEmpty();
        exportedType.Instances.Should().ContainSingle().Which.Cost.Should().Be(99.90m);

        export.Memberships.Should().ContainSingle().Which.HouseName.Should().Be("Maison partagée");
        export.InvitationsSent.Should().ContainSingle().Which.HouseName.Should().Be("Ma maison");
        export.InvitationsReceived.Should().ContainSingle().Which.HouseName.Should().Be("Maison partagée");
        export.ApiKeys.Should().ContainSingle().Which.Prefix.Should().Be("hf_abc");
        export.Sessions.Should().ContainSingle().Which.CreatedByIp.Should().Be("203.0.113.7");
        export.AuditLogs.Should().Contain(a => a.Action == "Update");

        // Art. 15(1)(a)-(h) : le volet « informations » est ce qui distingue un export
        // d'un simple extrait de base.
        export.Information.ContactEmail.Should().Be(GdprPolicy.PrivacyContactEmail);
        export.Information.PolicyVersion.Should().Be(GdprPolicy.CurrentPolicyVersion);
        export.Information.Purposes.Should().NotBeEmpty();
        export.Information.LegalBases.Should().NotBeEmpty();
        export.Information.Recipients.Should().NotBeEmpty();
        export.Information.Retention.Should().NotBeEmpty();
        export.Information.Rights.Should().NotBeEmpty();
        export.Information.SupervisoryAuthorities.Should().HaveCountGreaterThanOrEqualTo(2);
        export.Information.PortabilityScope.Should().Contain("profile").And.Contain("houses");
    }

    [Fact]
    public async Task ExportDataAsync_NeverLeaksSecretsOrThirdPartyIdentities()
    {
        using var context = new HouseFlowDbContext(_options);
        var user = NewUser("me@example.com");
        var otherOwner = NewUser("neighbour@example.com");
        otherOwner.FirstName = "Zoé";
        otherOwner.LastName = "Voisine";
        var sharedHouse = NewHouse(otherOwner.Id, "Maison partagée");

        context.Users.AddRange(user, otherOwner);
        context.Houses.Add(sharedHouse);
        context.HouseMembers.AddRange(
            NewMember(sharedHouse.Id, otherOwner.Id, HouseRole.Owner),
            NewMember(sharedHouse.Id, user.Id, HouseRole.CollaboratorRW));
        context.RefreshTokens.Add(new RefreshToken { Id = Guid.NewGuid(), UserId = user.Id, Token = "super-secret-token", ReplacedByToken = "rotated-secret-token", ExpiresAt = DateTime.UtcNow.AddDays(7), CreatedAt = DateTime.UtcNow });
        context.ApiKeys.Add(new ApiKey { Id = Guid.NewGuid(), UserId = user.Id, Name = "CI", Prefix = "hf_abc", KeyHash = "super-secret-hash", CreatedAt = DateTime.UtcNow });
        context.Invitations.Add(new Invitation { Id = Guid.NewGuid(), Token = "super-secret-invite", Role = HouseRole.CollaboratorRW, HouseId = sharedHouse.Id, CreatedByUserId = user.Id, ExpiresAt = DateTime.UtcNow.AddDays(7), CreatedAt = DateTime.UtcNow });
        await context.SaveChangesAsync();

        var export = await CreateService(context).ExportDataAsync(user.Id);
        var json = JsonSerializer.Serialize(export, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        json.Should().NotContain(user.PasswordHash);
        json.Should().NotContain("super-secret-token");
        json.Should().NotContain("rotated-secret-token");
        json.Should().NotContain("super-secret-hash");
        json.Should().NotContain("super-secret-invite");

        // Art. 15(4) / 20(4) : le droit d'accès ne porte pas atteinte aux droits des tiers.
        json.Should().NotContain("neighbour@example.com");
        json.Should().NotContain("Voisine");

        // La maison partagée reste identifiée par son nom et le rôle de l'utilisateur.
        export.Memberships.Should().ContainSingle().Which.Role.Should().Be("CollaboratorRW");
    }

    [Fact]
    public async Task ExportDataAsync_LogsAnAuditEntry()
    {
        using var context = new HouseFlowDbContext(_options);
        var user = NewUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        await CreateService(context).ExportDataAsync(user.Id, "203.0.113.7");

        var audit = await context.AuditLogs.AsNoTracking().SingleAsync(a => a.Action == "DataExport");
        audit.UserId.Should().Be(user.Id);
        audit.Username.Should().Be(user.Email);
        audit.IpAddress.Should().Be("203.0.113.7");
    }

    [Fact]
    public async Task ExportDataAsync_TwiceWithinAnHour_ThrowsTooManyRequests()
    {
        using var context = new HouseFlowDbContext(_options);
        var user = NewUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        await service.ExportDataAsync(user.Id);

        var act = () => service.ExportDataAsync(user.Id);

        var assertion = await act.Should().ThrowAsync<TooManyRequestsException>();
        assertion.Which.RetryAfterSeconds.Should().BeGreaterThan(0).And.BeLessThanOrEqualTo(3600);
    }

    [Fact]
    public async Task ExportDataAsync_AfterCooldown_Succeeds()
    {
        using var context = new HouseFlowDbContext(_options);
        var user = NewUser();
        context.Users.Add(user);
        context.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            EntityType = "User",
            EntityId = user.Id.ToString(),
            Action = "DataExport",
            UserId = user.Id,
            Username = user.Email,
            Timestamp = DateTime.UtcNow.AddHours(-2)
        });
        await context.SaveChangesAsync();

        var export = await CreateService(context).ExportDataAsync(user.Id);

        export.Profile.Email.Should().Be(user.Email);
        (await context.AuditLogs.CountAsync(a => a.Action == "DataExport")).Should().Be(2);
    }

    [Fact]
    public void CsvExportWriter_ProducesOneEntryPerCategoryPlusReadme()
    {
        var export = new HouseFlow.Application.DTOs.UserDataExportDto(
            ExportedAt: DateTime.UtcNow,
            FormatVersion: "1.0",
            Profile: new(Guid.NewGuid(), "me@example.com", "Alice", "Martin", DateTime.UtcNow, null, null),
            Preferences: new("system", "fr"),
            Consent: new(DateTime.UtcNow, GdprPolicy.CurrentPolicyVersion),
            Houses: [],
            Memberships: [],
            InvitationsSent: [],
            InvitationsReceived: [],
            ApiKeys: [],
            Sessions: [],
            AuditLogs: [],
            Information: new("HouseFlow", GdprPolicy.PrivacyContactEmail, GdprPolicy.CurrentPolicyVersion,
                [new("p", "p")], [new("c", "c")], [new("l", "l")], [new("r", "r")], [new("d", "d")], [new("g", "g")],
                [new("CNIL", "France", "https://www.cnil.fr")],
                new("s", "s"), new("t", "t"), new("a", "a"), ["profile"]));

        var bytes = CsvExportWriter.CreateZipArchive(export);

        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(bytes));
        archive.Entries.Select(e => e.FullName).Should().BeEquivalentTo(
            "profile.csv", "houses.csv", "devices.csv", "maintenance_types.csv", "maintenance_instances.csv",
            "memberships.csv", "invitations.csv", "api_keys.csv", "sessions.csv", "audit_logs.csv", "README.txt");
    }

    [Fact]
    public void CsvExportWriter_NeutralisesSpreadsheetFormulas_CsvInjection()
    {
        // Une note saisie par un collaborateur ne doit pas pouvoir s'exécuter dans le tableur du
        // propriétaire qui ouvre son export (OWASP CSV injection).
        var instance = new HouseFlow.Application.DTOs.ExportMaintenanceInstanceDto(
            Guid.NewGuid(), DateTime.UtcNow, 10m, "=cmd|'/c calc'!A1", "+HYPERLINK(\"http://evil\")", DateTime.UtcNow);
        var type = new HouseFlow.Application.DTOs.ExportMaintenanceTypeDto(Guid.NewGuid(), "Entretien", "Annual", null, "pending", [instance]);
        var device = new HouseFlow.Application.DTOs.ExportDeviceDto(Guid.NewGuid(), "-Chaudière", "GasBoiler", null, null, null, DateTime.UtcNow, [type]);
        var house = new HouseFlow.Application.DTOs.ExportHouseDto(Guid.NewGuid(), "@Maison", null, null, null, null, DateTime.UtcNow, [device]);

        var export = new HouseFlow.Application.DTOs.UserDataExportDto(
            ExportedAt: DateTime.UtcNow,
            FormatVersion: "1.0",
            Profile: new(Guid.NewGuid(), "me@example.com", "Alice", "Martin", DateTime.UtcNow, null, null),
            Preferences: new("system", "fr"),
            Consent: new(DateTime.UtcNow, GdprPolicy.CurrentPolicyVersion),
            Houses: [house],
            Memberships: [],
            InvitationsSent: [],
            InvitationsReceived: [],
            ApiKeys: [],
            Sessions: [],
            AuditLogs: [],
            Information: new("HouseFlow", GdprPolicy.PrivacyContactEmail, GdprPolicy.CurrentPolicyVersion,
                [new("p", "p")], [new("c", "c")], [new("l", "l")], [new("r", "r")], [new("d", "d")], [new("g", "g")],
                [new("CNIL", "France", "https://www.cnil.fr")],
                new("s", "s"), new("t", "t"), new("a", "a"), ["profile"]));

        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(CsvExportWriter.CreateZipArchive(export)));
        string Read(string name) { using var r = new StreamReader(archive.GetEntry(name)!.Open()); return r.ReadToEnd(); }

        var instances = Read("maintenance_instances.csv");
        instances.Should().Contain("'=cmd|'/c calc'!A1").And.NotContain(",=cmd");
        instances.Should().Contain("'+HYPERLINK");
        Read("devices.csv").Should().Contain("'-Chaudière");
        Read("houses.csv").Should().Contain("'@Maison");
    }
}
