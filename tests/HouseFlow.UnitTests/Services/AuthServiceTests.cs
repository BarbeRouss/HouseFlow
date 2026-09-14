using FluentAssertions;
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
        var request = new RegisterRequestDto(firstName: "Test", lastName: "User", email: "test@example.com", password: "Password123!");

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

        await authService.RegisterAsync(new RegisterRequestDto(firstName: "User", lastName: "One", email: "test@example.com", password: "Password123!"), "127.0.0.1");

        // Act & Assert
        var act = async () => await authService.RegisterAsync(
            new RegisterRequestDto(firstName: "User", lastName: "Two", email: "test@example.com", password: "Password456!"), "127.0.0.1");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already registered*");
    }

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ShouldReturnToken()
    {
        // Arrange
        using var context = new HouseFlowDbContext(_dbContextOptions);
        var authService = new AuthService(context, _mockConfiguration.Object, _mockLogger.Object);

        await authService.RegisterAsync(new RegisterRequestDto(firstName: "Test", lastName: "User", email: "test@example.com", password: "Password123!"), "127.0.0.1");

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

        await authService.RegisterAsync(new RegisterRequestDto(firstName: "Test", lastName: "User", email: "test@example.com", password: "Password123!"), "127.0.0.1");

        // Act & Assert
        var act = async () => await authService.LoginAsync(
            new LoginRequestDto(email: "test@example.com", password: "WrongPassword!", rememberMe: false), "127.0.0.1");

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
            new RegisterRequestDto(firstName: "Test", lastName: "User", email: "test@example.com", password: "Password123!"), "127.0.0.1");

        // Act
        var result = await authService.RefreshTokenAsync(registerResult.RefreshToken!, "127.0.0.1");

        // Assert
        result.Should().NotBeNull();
        result.AccessToken.Should().NotBeNullOrEmpty();
        result.RefreshToken.Should().NotBeNullOrEmpty();
        result.RefreshToken.Should().NotBe(registerResult.RefreshToken); // New token should be different
    }

    #region Session lifetime / families / reuse detection (#164)

    private static RegisterRequestDto Registration(string email = "test@example.com") =>
        new(firstName: "Test", lastName: "User", email: email, password: "Password123!");

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
        var token = await context.RefreshTokens.SingleAsync(rt => rt.Token == result.RefreshToken);
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
        var token = await context.RefreshTokens.SingleAsync(rt => rt.Token == result.RefreshToken);
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
        var first = await context.RefreshTokens.AsNoTracking().SingleAsync(rt => rt.Token == login.RefreshToken);

        var refreshed = await authService.RefreshTokenAsync(login.RefreshToken!, "127.0.0.1");

        var second = await context.RefreshTokens.SingleAsync(rt => rt.Token == refreshed.RefreshToken);
        second.FamilyId.Should().Be(first.FamilyId);
        second.RememberMe.Should().BeTrue();
        second.ExpiresAt.Should().BeOnOrAfter(first.ExpiresAt);
        refreshed.RefreshCookieExpiresAt.Should().Be(second.ExpiresAt);
        var rotated = await context.RefreshTokens.SingleAsync(rt => rt.Token == login.RefreshToken);
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
        replay.RefreshToken.Should().Be(a2, "the current token is re-issued instead of rotating again");
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
        var rotated = await context.RefreshTokens.SingleAsync(rt => rt.Token == a1);
        rotated.RevokedAt = DateTime.UtcNow - AuthService.RotationGracePeriod - TimeSpan.FromMinutes(1);
        await context.SaveChangesAsync();

        // A stolen cookie is replayed: the whole family of device A goes down.
        var replay = async () => await authService.RefreshTokenAsync(a1, "6.6.6.6");
        await replay.Should().ThrowAsync<UnauthorizedAccessException>();

        var familyA = await context.RefreshTokens.Where(rt => rt.FamilyId == rotated.FamilyId).ToListAsync();
        familyA.Should().OnlyContain(rt => rt.RevokedAt != null);
        familyA.Single(rt => rt.Token == a2).ReasonRevoked.Should().Be("Reuse detected");
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
            new RegisterRequestDto(firstName: "Test", lastName: "User", email: "test@example.com", password: "Password123!"), "127.0.0.1");

        // Act
        await authService.RevokeTokenAsync(registerResult.RefreshToken!, "127.0.0.1");

        // Assert - trying to use revoked token should throw
        var act = async () => await authService.RefreshTokenAsync(registerResult.RefreshToken!, "127.0.0.1");
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}
