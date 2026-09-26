using FluentAssertions;
using HouseFlow.Application.Common;
using HouseFlow.Application.DTOs;
using HouseFlow.Core.Entities;
using HouseFlow.Infrastructure.Data;
using HouseFlow.Application.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using BCryptNet = BCrypt.Net.BCrypt;

namespace HouseFlow.UnitTests.Services;

public class AuthServiceTests
{
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<ILogger<AuthService>> _mockLogger;
    private readonly DbContextOptions<HouseFlowDbContext> _dbContextOptions;

    public AuthServiceTests()
    {
        _mockConfiguration = new Mock<IConfiguration>();
        _mockConfiguration.Setup(c => c["Jwt:Key"]).Returns("TestSecretKeyForJWTTokenGeneration123456TestSecretKeyForJWTTokenGeneration123456");
        _mockConfiguration.Setup(c => c["Jwt:Issuer"]).Returns("TestIssuer");
        _mockConfiguration.Setup(c => c["Jwt:Audience"]).Returns("TestAudience");
        _mockConfiguration.Setup(c => c["Jwt:RefreshTokenExpirationDays"]).Returns("7");

        _mockLogger = new Mock<ILogger<AuthService>>();

        _dbContextOptions = new DbContextOptionsBuilder<HouseFlowDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
    }

    [Fact]
    public async Task RegisterAsync_WithValidData_ShouldCreateUserAndDefaultHouse()
    {
        // Arrange
        using var context = new HouseFlowDbContext(_dbContextOptions);
        var authService = new AuthService(context, _mockConfiguration.Object, _mockLogger.Object);
        var request = new RegisterRequestDto(firstName: "Test", lastName: "User", email: "test@example.com", password: "Password123!", consentAccepted: true);

        // Act
        var result = await authService.RegisterAsync(request, "127.0.0.1");

        // Assert
        result.Should().NotBeNull();
        result.AccessToken.Should().NotBeNullOrEmpty();
        result.User.Email.Should().Be("test@example.com");
        result.User.FirstName.Should().Be("Test");
        result.User.LastName.Should().Be("User");

        var user = await context.Users.FirstOrDefaultAsync(u => u.Email == "test@example.com");
        user.Should().NotBeNull();

        // Verify default house was created
        var house = await context.Houses.FirstOrDefaultAsync(h => h.UserId == user!.Id);
        house.Should().NotBeNull();
        house!.Name.Should().Be("Ma maison");
    }

    [Fact]
    public async Task RegisterAsync_WithoutTermsAccepted_ShouldThrowAndCreateNothing()
    {
        // Arrange
        using var context = new HouseFlowDbContext(_dbContextOptions);
        var authService = new AuthService(context, _mockConfiguration.Object, _mockLogger.Object);
        var request = new RegisterRequestDto(firstName: "Test", lastName: "User", email: "refused@example.com", password: "Password123!", consentAccepted: false);

        // Act
        var act = async () => await authService.RegisterAsync(request, "127.0.0.1");

        // Assert — l'inscription est refusée AVANT toute écriture (RGPD Art. 6(1)(b) : l'acceptation
        // des CGU est une condition de conclusion du contrat).
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*accept the terms of service*");

        (await context.Users.AnyAsync(u => u.Email == "refused@example.com")).Should().BeFalse();
        (await context.Houses.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task RegisterAsync_ShouldStoreConsentTimestampAndPolicyVersion()
    {
        // Arrange
        using var context = new HouseFlowDbContext(_dbContextOptions);
        var authService = new AuthService(context, _mockConfiguration.Object, _mockLogger.Object);
        var before = DateTime.UtcNow;

        // Act
        var result = await authService.RegisterAsync(
            new RegisterRequestDto(firstName: "Test", lastName: "User", email: "consent@example.com", password: "Password123!", consentAccepted: true), "127.0.0.1");

        // Assert
        var user = await context.Users.FirstAsync(u => u.Email == "consent@example.com");
        user.ConsentGivenAt.Should().NotBeNull();
        user.ConsentGivenAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(DateTime.UtcNow);
        user.ConsentPolicyVersion.Should().Be(GdprPolicy.CurrentPolicyVersion);

        // Le frontend ne doit PAS afficher la bannière de ré-acceptation à un nouvel inscrit.
        result.User.ConsentRequired.Should().BeFalse();
    }

    [Fact]
    public async Task RegisterAsync_ShouldRecordConsentValuesAndIpInTheCreationAuditLog()
    {
        // Arrange
        using var context = new HouseFlowDbContext(_dbContextOptions);
        var authService = new AuthService(context, _mockConfiguration.Object, _mockLogger.Object);

        // Act
        await authService.RegisterAsync(
            new RegisterRequestDto(firstName: "Test", lastName: "User", email: "audited@example.com", password: "Password123!", consentAccepted: true), "203.0.113.7");

        // Assert — preuve Art. 7(1)/5(2) : l'audit de création porte la date, la version et l'IP.
        var audit = await context.AuditLogs
            .Where(a => a.EntityType == "User" && a.Action == "Added")
            .OrderByDescending(a => a.Timestamp)
            .FirstAsync();

        audit.NewValues.Should().Contain(nameof(User.ConsentGivenAt));
        audit.NewValues.Should().Contain(GdprPolicy.CurrentPolicyVersion);
        audit.IpAddress.Should().Be("203.0.113.7");
        // Minimisation : jamais de secret dans l'audit.
        audit.NewValues.Should().NotContain(nameof(User.PasswordHash));
    }

    [Fact]
    public async Task LoginAsync_ForUserWithoutConsent_ShouldReportConsentRequired()
    {
        // Arrange — un compte créé avant l'introduction des CGU versionnées.
        using var context = new HouseFlowDbContext(_dbContextOptions);
        var authService = new AuthService(context, _mockConfiguration.Object, _mockLogger.Object);

        await authService.RegisterAsync(
            new RegisterRequestDto(firstName: "Legacy", lastName: "User", email: "legacy@example.com", password: "Password123!", consentAccepted: true), "127.0.0.1");

        var user = await context.Users.FirstAsync(u => u.Email == "legacy@example.com");
        user.ConsentGivenAt = null;
        user.ConsentPolicyVersion = null;
        await context.SaveChangesAsync();

        // Act
        var result = await authService.LoginAsync(new LoginRequestDto(email: "legacy@example.com", password: "Password123!", rememberMe: false), "127.0.0.1");

        // Assert
        result.User.ConsentRequired.Should().BeTrue();
    }

    [Fact]
    public async Task LoginAsync_ForUserWithOutdatedPolicyVersion_ShouldReportConsentRequired()
    {
        // Arrange
        using var context = new HouseFlowDbContext(_dbContextOptions);
        var authService = new AuthService(context, _mockConfiguration.Object, _mockLogger.Object);

        await authService.RegisterAsync(
            new RegisterRequestDto(firstName: "Old", lastName: "Policy", email: "outdated@example.com", password: "Password123!", consentAccepted: true), "127.0.0.1");

        var user = await context.Users.FirstAsync(u => u.Email == "outdated@example.com");
        user.ConsentPolicyVersion = "1900-01-01";
        await context.SaveChangesAsync();

        // Act
        var login = await authService.LoginAsync(new LoginRequestDto(email: "outdated@example.com", password: "Password123!", rememberMe: false), "127.0.0.1");
        var refreshed = await authService.RefreshTokenAsync(login.RefreshToken!, "127.0.0.1");

        // Assert — la bannière reste affichée après un refresh de token.
        login.User.ConsentRequired.Should().BeTrue();
        refreshed.User.ConsentRequired.Should().BeTrue();
    }

    [Fact]
    public async Task RegisterAsync_WithExistingEmail_ShouldThrowException()
    {
        // Arrange
        using var context = new HouseFlowDbContext(_dbContextOptions);
        var authService = new AuthService(context, _mockConfiguration.Object, _mockLogger.Object);

        await authService.RegisterAsync(new RegisterRequestDto(firstName: "User", lastName: "One", email: "test@example.com", password: "Password123!", consentAccepted: true), "127.0.0.1");

        // Act & Assert
        var act = async () => await authService.RegisterAsync(
            new RegisterRequestDto(firstName: "User", lastName: "Two", email: "test@example.com", password: "Password456!", consentAccepted: true), "127.0.0.1");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already registered*");
    }

    [Fact]
    public async Task LoginAsync_ShouldRecordLastLoginAt()
    {
        using var context = new HouseFlowDbContext(_dbContextOptions);
        var authService = new AuthService(context, _mockConfiguration.Object, _mockLogger.Object);
        await authService.RegisterAsync(new RegisterRequestDto(firstName: "Test", lastName: "User", email: "last@example.com", password: "Password123!", consentAccepted: true), "127.0.0.1");

        var before = DateTime.UtcNow;
        await authService.LoginAsync(new LoginRequestDto(email: "last@example.com", password: "Password123!", rememberMe: false), "127.0.0.1");

        var user = await context.Users.AsNoTracking().SingleAsync(u => u.Email == "last@example.com");
        user.LastLoginAt.Should().NotBeNull();
        user.LastLoginAt!.Value.Should().BeOnOrAfter(before.AddSeconds(-1));
    }

    [Fact]
    public async Task LoginAsync_WhenProcessingRestricted_ShouldBeRefused_Art18()
    {
        using var context = new HouseFlowDbContext(_dbContextOptions);
        var authService = new AuthService(context, _mockConfiguration.Object, _mockLogger.Object);
        await authService.RegisterAsync(new RegisterRequestDto(firstName: "Test", lastName: "User", email: "restricted@example.com", password: "Password123!", consentAccepted: true), "127.0.0.1");

        var user = await context.Users.SingleAsync(u => u.Email == "restricted@example.com");
        user.ProcessingRestrictedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        var act = async () => await authService.LoginAsync(new LoginRequestDto(email: "restricted@example.com", password: "Password123!", rememberMe: false), "127.0.0.1");

        await act.Should().ThrowAsync<UnauthorizedAccessException>().WithMessage("*restricted*");
        // Données conservées intactes (Art. 18(2))
        (await context.Users.AsNoTracking().AnyAsync(u => u.Email == "restricted@example.com")).Should().BeTrue();
    }

    [Fact]
    public async Task RegisterAsync_AuditEntries_ShouldCarryTheNewUserId()
    {
        using var context = new HouseFlowDbContext(_dbContextOptions);
        var authService = new AuthService(context, _mockConfiguration.Object, _mockLogger.Object);
        var result = await authService.RegisterAsync(new RegisterRequestDto(firstName: "Test", lastName: "User", email: "audit@example.com", password: "Password123!", consentAccepted: true), "127.0.0.1");

        var orphan = await context.AuditLogs.AsNoTracking()
            .CountAsync(a => a.Username == "audit@example.com" && a.UserId == null);
        orphan.Should().Be(0, "every registration audit entry must be attributable (and thus anonymisable) via UserId");
        (await context.AuditLogs.AsNoTracking().AnyAsync(a => a.UserId == result.User.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ShouldReturnToken()
    {
        // Arrange
        using var context = new HouseFlowDbContext(_dbContextOptions);
        var authService = new AuthService(context, _mockConfiguration.Object, _mockLogger.Object);

        await authService.RegisterAsync(new RegisterRequestDto(firstName: "Test", lastName: "User", email: "test@example.com", password: "Password123!", consentAccepted: true), "127.0.0.1");

        // Act
        var result = await authService.LoginAsync(new LoginRequestDto(email: "test@example.com", password: "Password123!", rememberMe: false), "127.0.0.1");

        // Assert
        result.Should().NotBeNull();
        result.AccessToken.Should().NotBeNullOrEmpty();
        result.User.Email.Should().Be("test@example.com");
    }

    [Fact]
    public async Task LoginAsync_WithInvalidPassword_ShouldThrowException()
    {
        // Arrange
        using var context = new HouseFlowDbContext(_dbContextOptions);
        var authService = new AuthService(context, _mockConfiguration.Object, _mockLogger.Object);

        await authService.RegisterAsync(new RegisterRequestDto(firstName: "Test", lastName: "User", email: "test@example.com", password: "Password123!", consentAccepted: true), "127.0.0.1");

        // Act & Assert
        var act = async () => await authService.LoginAsync(
            new LoginRequestDto(email: "test@example.com", password: "WrongPassword!", rememberMe: false), "127.0.0.1");

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Invalid email or password");
    }

    [Fact]
    public async Task LoginAsync_WithLegacyWeakPassword_ShouldStillSucceed()
    {
        // Arrange: an account created before the 8-char/complexity policy (#156) was enforced.
        // The policy only applies at registration/password-change time, never at login, so an
        // existing weak password must keep working.
        using var context = new HouseFlowDbContext(_dbContextOptions);
        var legacyUser = new User
        {
            Id = Guid.NewGuid(),
            Email = "legacy@example.com",
            FirstName = "Legacy",
            LastName = "User",
            PasswordHash = BCryptNet.HashPassword("motdepasse1"), // weak by the new policy
            CreatedAt = DateTime.UtcNow
        };
        context.Users.Add(legacyUser);
        await context.SaveChangesAsync();

        var authService = new AuthService(context, _mockConfiguration.Object, _mockLogger.Object);

        // Act
        var result = await authService.LoginAsync(
            new LoginRequestDto(email: "legacy@example.com", password: "motdepasse1", rememberMe: false), "127.0.0.1");

        // Assert
        result.Should().NotBeNull();
        result.AccessToken.Should().NotBeNullOrEmpty();
        result.User.Email.Should().Be("legacy@example.com");
    }

    [Fact]
    public async Task RefreshTokenAsync_WithValidToken_ShouldReturnNewTokens()
    {
        // Arrange
        using var context = new HouseFlowDbContext(_dbContextOptions);
        var authService = new AuthService(context, _mockConfiguration.Object, _mockLogger.Object);

        var registerResult = await authService.RegisterAsync(
            new RegisterRequestDto(firstName: "Test", lastName: "User", email: "test@example.com", password: "Password123!", consentAccepted: true), "127.0.0.1");

        // Act
        var result = await authService.RefreshTokenAsync(registerResult.RefreshToken!, "127.0.0.1");

        // Assert
        result.Should().NotBeNull();
        result.AccessToken.Should().NotBeNullOrEmpty();
        result.RefreshToken.Should().NotBeNullOrEmpty();
        result.RefreshToken.Should().NotBe(registerResult.RefreshToken); // New token should be different
    }

    // ── LastLoginAt : le rafraîchissement compte comme une activité ──────────────
    //
    // Une session « Se souvenir de moi » est glissante sur un an. Sans ces deux tests,
    // rien n'empêcherait une régression qui laisserait LastLoginAt figé sur un compte
    // pourtant utilisé tous les jours — et la purge des comptes inactifs à 3 ans
    // (politique de conservation § 5) supprimerait ce compte actif.

    [Fact]
    public async Task RefreshTokenAsync_WhenLastLoginIsStale_ShouldRefreshIt()
    {
        // Arrange
        using var context = new HouseFlowDbContext(_dbContextOptions);
        var authService = new AuthService(context, _mockConfiguration.Object, _mockLogger.Object);

        var registered = await authService.RegisterAsync(
            new RegisterRequestDto(firstName: "Active", lastName: "User", email: "stale@example.com", password: "Password123!", consentAccepted: true), "127.0.0.1");

        // L'utilisateur n'a pas ressaisi son mot de passe depuis deux ans : il ne repasse
        // jamais par LoginAsync, seul son cookie est renouvelé.
        var user = await context.Users.FirstAsync(u => u.Email == "stale@example.com");
        var staleDate = DateTime.UtcNow.AddYears(-2);
        user.LastLoginAt = staleDate;
        await context.SaveChangesAsync();

        // Act
        await authService.RefreshTokenAsync(registered.RefreshToken!, "127.0.0.1");

        // Assert
        var reloaded = await context.Users.FirstAsync(u => u.Email == "stale@example.com");
        reloaded.LastLoginAt.Should().NotBeNull();
        reloaded.LastLoginAt!.Value.Should().BeAfter(staleDate)
            .And.BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task RefreshTokenAsync_WhenLastLoginIsRecent_ShouldLeaveItUntouched()
    {
        // Arrange
        using var context = new HouseFlowDbContext(_dbContextOptions);
        var authService = new AuthService(context, _mockConfiguration.Object, _mockLogger.Object);

        var registered = await authService.RegisterAsync(
            new RegisterRequestDto(firstName: "Active", lastName: "User", email: "fresh@example.com", password: "Password123!", consentAccepted: true), "127.0.0.1");

        // Une heure, soit bien en deçà du seuil : le rafraîchissement courant (toutes les
        // 15 minutes) ne doit produire aucune écriture.
        var recentDate = DateTime.UtcNow.AddHours(-1);
        var user = await context.Users.FirstAsync(u => u.Email == "fresh@example.com");
        user.LastLoginAt = recentDate;
        await context.SaveChangesAsync();

        // Act
        await authService.RefreshTokenAsync(registered.RefreshToken!, "127.0.0.1");

        // Assert
        var reloaded = await context.Users.FirstAsync(u => u.Email == "fresh@example.com");
        reloaded.LastLoginAt.Should().BeCloseTo(recentDate, TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public async Task RefreshTokenAsync_ShouldNotWriteLastLoginToTheAuditTrail()
    {
        // Arrange — un horodatage technique n'a rien à faire dans le journal d'audit :
        // l'auditer produirait une entrée quotidienne par utilisateur, pour rien.
        using var context = new HouseFlowDbContext(_dbContextOptions);
        var authService = new AuthService(context, _mockConfiguration.Object, _mockLogger.Object);

        var registered = await authService.RegisterAsync(
            new RegisterRequestDto(firstName: "Audit", lastName: "User", email: "audit-lastlogin@example.com", password: "Password123!", consentAccepted: true), "127.0.0.1");

        var user = await context.Users.FirstAsync(u => u.Email == "audit-lastlogin@example.com");
        user.LastLoginAt = DateTime.UtcNow.AddYears(-2);
        await context.SaveChangesAsync();

        // Act
        await authService.RefreshTokenAsync(registered.RefreshToken!, "127.0.0.1");

        // Assert
        var audited = await context.AuditLogs
            .Where(a => a.EntityType == "User")
            .ToListAsync();
        audited.Should().NotContain(a =>
            a.ChangedProperties != null && a.ChangedProperties.Contains(nameof(User.LastLoginAt)));
    }

    #region Session lifetime / families / reuse detection (#164)

    private static RegisterRequestDto Registration(string email = "test@example.com") =>
        new(firstName: "Test", lastName: "User", email: email, password: "Password123!", consentAccepted: true);

    private static LoginRequestDto Login(bool rememberMe, string email = "test@example.com") =>
        new(email: email, password: "Password123!", rememberMe: rememberMe);

    [Fact]
    public async Task LoginAsync_WithRememberMe_IssuesYearLongPersistentSession()
    {
        using var context = new HouseFlowDbContext(_dbContextOptions);
        var authService = new AuthService(context, _mockConfiguration.Object, _mockLogger.Object);
        await authService.RegisterAsync(Registration(), "127.0.0.1");

        var result = await authService.LoginAsync(Login(rememberMe: true), "127.0.0.1");

        var expected = DateTime.UtcNow + AuthService.RememberMeLifetime;
        result.RefreshCookieExpiresAt.Should().BeCloseTo(expected, TimeSpan.FromMinutes(1));
        var token = await context.RefreshTokens.SingleAsync(rt => rt.Token == TokenHasher.Hash(result.RefreshToken!));
        token.RememberMe.Should().BeTrue();
        token.ExpiresAt.Should().BeCloseTo(expected, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task LoginAsync_WithoutRememberMe_IssuesShortSessionWithSessionCookie()
    {
        using var context = new HouseFlowDbContext(_dbContextOptions);
        var authService = new AuthService(context, _mockConfiguration.Object, _mockLogger.Object);
        await authService.RegisterAsync(Registration(), "127.0.0.1");

        var result = await authService.LoginAsync(Login(rememberMe: false), "127.0.0.1");

        result.RefreshCookieExpiresAt.Should().BeNull("a plain session uses a browser-session cookie");
        var token = await context.RefreshTokens.SingleAsync(rt => rt.Token == TokenHasher.Hash(result.RefreshToken!));
        token.RememberMe.Should().BeFalse();
        token.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow + AuthService.SessionLifetime, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task RefreshTokenAsync_KeepsFamilyAndRememberMe_AndSlidesExpiry()
    {
        using var context = new HouseFlowDbContext(_dbContextOptions);
        var authService = new AuthService(context, _mockConfiguration.Object, _mockLogger.Object);
        await authService.RegisterAsync(Registration(), "127.0.0.1");
        var login = await authService.LoginAsync(Login(rememberMe: true), "127.0.0.1");
        var first = await context.RefreshTokens.AsNoTracking().SingleAsync(rt => rt.Token == TokenHasher.Hash(login.RefreshToken!));

        var refreshed = await authService.RefreshTokenAsync(login.RefreshToken!, "127.0.0.1");

        var second = await context.RefreshTokens.SingleAsync(rt => rt.Token == TokenHasher.Hash(refreshed.RefreshToken!));
        second.FamilyId.Should().Be(first.FamilyId);
        second.RememberMe.Should().BeTrue();
        second.ExpiresAt.Should().BeOnOrAfter(first.ExpiresAt);
        refreshed.RefreshCookieExpiresAt.Should().Be(second.ExpiresAt);
        var rotated = await context.RefreshTokens.SingleAsync(rt => rt.Token == TokenHasher.Hash(login.RefreshToken!));
        rotated.RevokedAt.Should().NotBeNull();
        rotated.ReplacedByToken.Should().Be(second.Token);
    }

    [Fact]
    public async Task RefreshTokenAsync_RotatedTokenReusedWithinGrace_ReturnsCurrentTokenWithoutRevoking()
    {
        using var context = new HouseFlowDbContext(_dbContextOptions);
        var authService = new AuthService(context, _mockConfiguration.Object, _mockLogger.Object);
        var registered = await authService.RegisterAsync(Registration(), "127.0.0.1");
        var a1 = registered.RefreshToken!;
        var a2 = (await authService.RefreshTokenAsync(a1, "127.0.0.1")).RefreshToken!;

        // Second tab racing with the same cookie: not a theft.
        var replay = await authService.RefreshTokenAsync(a1, "127.0.0.1");

        replay.AccessToken.Should().NotBeNullOrEmpty();
        // La base ne garde que des empreintes : le token courant n'étant pas rejouable, l'onglet
        // perdant reçoit un frère de la même famille, et celui de l'onglet gagnant reste valide.
        replay.RefreshToken.Should().NotBe(a2);
        var sibling = await context.RefreshTokens.AsNoTracking()
            .SingleAsync(rt => rt.Token == TokenHasher.Hash(replay.RefreshToken!));
        var current = await context.RefreshTokens.AsNoTracking()
            .SingleAsync(rt => rt.Token == TokenHasher.Hash(a2));
        sibling.FamilyId.Should().Be(current.FamilyId);
        current.RevokedAt.Should().BeNull("the winning tab must not be logged out");

        var a3 = await authService.RefreshTokenAsync(a2, "127.0.0.1");
        a3.RefreshToken.Should().NotBe(a2);
    }

    [Fact]
    public async Task RefreshTokenAsync_RotatedTokenReusedOutsideGrace_RevokesFamilyButNotOtherSessions()
    {
        using var context = new HouseFlowDbContext(_dbContextOptions);
        var authService = new AuthService(context, _mockConfiguration.Object, _mockLogger.Object);
        await authService.RegisterAsync(Registration(), "127.0.0.1");
        var deviceA = await authService.LoginAsync(Login(rememberMe: true), "10.0.0.1");
        var deviceB = await authService.LoginAsync(Login(rememberMe: true), "10.0.0.2");
        var a1 = deviceA.RefreshToken!;
        var a2 = (await authService.RefreshTokenAsync(a1, "10.0.0.1")).RefreshToken!;

        // Push the rotation out of the grace window.
        var rotated = await context.RefreshTokens.SingleAsync(rt => rt.Token == TokenHasher.Hash(a1));
        rotated.RevokedAt = DateTime.UtcNow - AuthService.RotationGracePeriod - TimeSpan.FromMinutes(1);
        await context.SaveChangesAsync();

        // A stolen cookie is replayed: the whole family of device A goes down.
        var replay = async () => await authService.RefreshTokenAsync(a1, "6.6.6.6");
        await replay.Should().ThrowAsync<UnauthorizedAccessException>();

        var familyA = await context.RefreshTokens.Where(rt => rt.FamilyId == rotated.FamilyId).ToListAsync();
        familyA.Should().OnlyContain(rt => rt.RevokedAt != null);
        familyA.Single(rt => rt.Token == TokenHasher.Hash(a2)).ReasonRevoked.Should().Be("Reuse detected");
        var legit = async () => await authService.RefreshTokenAsync(a2, "10.0.0.1");
        await legit.Should().ThrowAsync<UnauthorizedAccessException>();

        // Device B is untouched.
        var b2 = await authService.RefreshTokenAsync(deviceB.RefreshToken!, "10.0.0.2");
        b2.AccessToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task LoginAsync_BeyondMaxSessions_EvictsLeastRecentlyUsedSession()
    {
        using var context = new HouseFlowDbContext(_dbContextOptions);
        var authService = new AuthService(context, _mockConfiguration.Object, _mockLogger.Object);
        var sessions = new List<string> { (await authService.RegisterAsync(Registration(), "127.0.0.1")).RefreshToken! };
        for (var i = 1; i < AuthService.MaxSessionsPerUser; i++)
            sessions.Add((await authService.LoginAsync(Login(rememberMe: false), $"10.0.0.{i}")).RefreshToken!);

        // Using session #1 makes it the most recently used one; session #2 becomes the oldest.
        sessions[0] = (await authService.RefreshTokenAsync(sessions[0], "127.0.0.1")).RefreshToken!;

        // One session too many.
        var extra = await authService.LoginAsync(Login(rememberMe: false), "10.0.0.99");

        var evicted = async () => await authService.RefreshTokenAsync(sessions[1], "10.0.0.1");
        await evicted.Should().ThrowAsync<UnauthorizedAccessException>();
        (await authService.RefreshTokenAsync(sessions[0], "127.0.0.1")).AccessToken.Should().NotBeNullOrEmpty();
        (await authService.RefreshTokenAsync(sessions[2], "10.0.0.2")).AccessToken.Should().NotBeNullOrEmpty();
        (await authService.RefreshTokenAsync(extra.RefreshToken!, "10.0.0.99")).AccessToken.Should().NotBeNullOrEmpty();
        var active = await context.RefreshTokens.Where(rt => rt.RevokedAt == null).CountAsync();
        active.Should().Be(AuthService.MaxSessionsPerUser);
    }

    #endregion

    [Fact]
    public async Task RevokeTokenAsync_WithValidToken_ShouldRevokeToken()
    {
        // Arrange
        using var context = new HouseFlowDbContext(_dbContextOptions);
        var authService = new AuthService(context, _mockConfiguration.Object, _mockLogger.Object);

        var registerResult = await authService.RegisterAsync(
            new RegisterRequestDto(firstName: "Test", lastName: "User", email: "test@example.com", password: "Password123!", consentAccepted: true), "127.0.0.1");

        // Act
        await authService.RevokeTokenAsync(registerResult.RefreshToken!, "127.0.0.1");

        // Assert - trying to use revoked token should throw
        var act = async () => await authService.RefreshTokenAsync(registerResult.RefreshToken!, "127.0.0.1");
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    /// <summary>
    /// RGPD Art. 32(1)(a) — un vol de la base ne doit pas permettre de forger une session :
    /// seule l'empreinte du refresh token est persistée, jamais sa valeur.
    /// </summary>
    [Fact]
    public async Task RegisterAsync_ShouldStoreOnlyTheHashOfTheRefreshToken()
    {
        // Arrange
        using var context = new HouseFlowDbContext(_dbContextOptions);
        var authService = new AuthService(context, _mockConfiguration.Object, _mockLogger.Object);

        // Act
        var registerResult = await authService.RegisterAsync(
            new RegisterRequestDto(firstName: "Test", lastName: "User", email: "test@example.com", password: "Password123!", consentAccepted: true), "127.0.0.1");

        // Assert
        var stored = await context.RefreshTokens.SingleAsync();
        stored.Token.Should().NotBe(registerResult.RefreshToken);
        stored.Token.Should().Be(TokenHasher.Hash(registerResult.RefreshToken!));
    }

    [Fact]
    public async Task RefreshTokenAsync_ShouldStoreTheRotationChainAsHashes()
    {
        // Arrange
        using var context = new HouseFlowDbContext(_dbContextOptions);
        var authService = new AuthService(context, _mockConfiguration.Object, _mockLogger.Object);

        var registerResult = await authService.RegisterAsync(
            new RegisterRequestDto(firstName: "Test", lastName: "User", email: "test@example.com", password: "Password123!", consentAccepted: true), "127.0.0.1");

        // Act
        var refreshResult = await authService.RefreshTokenAsync(registerResult.RefreshToken!, "127.0.0.1");

        // Assert
        var rotated = await context.RefreshTokens
            .SingleAsync(rt => rt.Token == TokenHasher.Hash(registerResult.RefreshToken!));

        rotated.RevokedAt.Should().NotBeNull();
        rotated.ReplacedByToken.Should().Be(TokenHasher.Hash(refreshResult.RefreshToken!));
        rotated.ReplacedByToken.Should().NotBe(refreshResult.RefreshToken);
    }

    /// <summary>
    /// RGPD Art. 32(1)(b) — rotation avec détection de réutilisation : présenter un token
    /// déjà rotaté, hors de la fenêtre de grâce, signale une copie en circulation ; toute la
    /// famille tombe, y compris le token courant pourtant valide.
    /// </summary>
    [Fact]
    public async Task RefreshTokenAsync_WithReusedToken_ShouldRevokeTheWholeFamily()
    {
        // Arrange
        using var context = new HouseFlowDbContext(_dbContextOptions);
        var authService = new AuthService(context, _mockConfiguration.Object, _mockLogger.Object);

        var registerResult = await authService.RegisterAsync(
            new RegisterRequestDto(firstName: "Test", lastName: "User", email: "test@example.com", password: "Password123!", consentAccepted: true), "127.0.0.1");

        // Rotation légitime : le token initial est désormais remplacé.
        var refreshResult = await authService.RefreshTokenAsync(registerResult.RefreshToken!, "127.0.0.1");

        // Le rejeu est sorti de la fenêtre de grâce : ce n'est plus une course entre onglets.
        var rotated = await context.RefreshTokens
            .SingleAsync(rt => rt.Token == TokenHasher.Hash(registerResult.RefreshToken!));
        rotated.RevokedAt = DateTime.UtcNow - AuthService.RotationGracePeriod - TimeSpan.FromMinutes(1);
        await context.SaveChangesAsync();

        // Act — le token initial est rejoué (vol présumé).
        var act = async () => await authService.RefreshTokenAsync(registerResult.RefreshToken!, "10.0.0.1");

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>();

        var tokens = await context.RefreshTokens.Where(rt => rt.FamilyId == rotated.FamilyId).ToListAsync();
        tokens.Should().OnlyContain(rt => rt.RevokedAt != null, "toute la chaîne doit tomber");
        tokens.Should().Contain(rt => rt.ReasonRevoked == "Reuse detected");

        // Le token courant, pourtant valide, ne doit plus fonctionner.
        var afterBreach = async () => await authService.RefreshTokenAsync(refreshResult.RefreshToken!, "127.0.0.1");
        await afterBreach.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task RefreshTokenAsync_WithUnknownToken_ShouldThrow()
    {
        using var context = new HouseFlowDbContext(_dbContextOptions);
        var authService = new AuthService(context, _mockConfiguration.Object, _mockLogger.Object);

        var act = async () => await authService.RefreshTokenAsync("not-a-token", "127.0.0.1");

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}
