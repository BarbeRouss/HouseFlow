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
        var result = await authService.LoginAsync(new LoginRequestDto(email: "legacy@example.com", password: "Password123!"), "127.0.0.1");

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
        var login = await authService.LoginAsync(new LoginRequestDto(email: "outdated@example.com", password: "Password123!"), "127.0.0.1");
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
        await authService.LoginAsync(new LoginRequestDto(email: "last@example.com", password: "Password123!"), "127.0.0.1");

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

        var act = async () => await authService.LoginAsync(new LoginRequestDto(email: "restricted@example.com", password: "Password123!"), "127.0.0.1");

        await act.Should().ThrowAsync<UnauthorizedAccessException>().WithMessage("*restricted*");
        // Données conservées intactes (Art. 18(2))
        (await context.Users.AsNoTracking().AnyAsync(u => u.Email == "restricted@example.com")).Should().BeTrue();
    }

    [Fact]
    public async Task LoginAsync_ShouldKeepAtMostFiveRefreshTokensPerUser()
    {
        using var context = new HouseFlowDbContext(_dbContextOptions);
        var authService = new AuthService(context, _mockConfiguration.Object, _mockLogger.Object);
        await authService.RegisterAsync(new RegisterRequestDto(firstName: "Test", lastName: "User", email: "cap@example.com", password: "Password123!", consentAccepted: true), "127.0.0.1");
        var user = await context.Users.AsNoTracking().SingleAsync(u => u.Email == "cap@example.com");

        for (var i = 0; i < 8; i++)
        {
            await authService.LoginAsync(new LoginRequestDto(email: "cap@example.com", password: "Password123!"), "127.0.0.1");
        }

        (await context.RefreshTokens.CountAsync(t => t.UserId == user.Id)).Should().BeLessThanOrEqualTo(5);
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
        var result = await authService.LoginAsync(new LoginRequestDto(email: "test@example.com", password: "Password123!"), "127.0.0.1");

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
            new LoginRequestDto(email: "test@example.com", password: "WrongPassword!"), "127.0.0.1");

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Invalid email or password");
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
    /// déjà rotaté signale une copie en circulation ; toute la famille est révoquée.
    /// </summary>
    [Fact]
    public async Task RefreshTokenAsync_WithReusedToken_ShouldRevokeEveryActiveTokenOfTheUser()
    {
        // Arrange
        using var context = new HouseFlowDbContext(_dbContextOptions);
        var authService = new AuthService(context, _mockConfiguration.Object, _mockLogger.Object);

        var registerResult = await authService.RegisterAsync(
            new RegisterRequestDto(firstName: "Test", lastName: "User", email: "test@example.com", password: "Password123!", consentAccepted: true), "127.0.0.1");

        // Rotation légitime : le token initial est désormais remplacé.
        var refreshResult = await authService.RefreshTokenAsync(registerResult.RefreshToken!, "127.0.0.1");

        // Act — le token initial est rejoué (vol présumé).
        var act = async () => await authService.RefreshTokenAsync(registerResult.RefreshToken!, "10.0.0.1");

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>();

        var tokens = await context.RefreshTokens.ToListAsync();
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
