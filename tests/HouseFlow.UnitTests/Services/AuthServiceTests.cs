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
