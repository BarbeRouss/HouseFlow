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
}
