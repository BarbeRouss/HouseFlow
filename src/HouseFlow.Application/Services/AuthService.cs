using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HouseFlow.Application.Common;
using HouseFlow.Application.DTOs;
using HouseFlow.Application.Interfaces;
using HouseFlow.Core.Entities;
using HouseFlow.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using BCryptNet = BCrypt.Net.BCrypt;

namespace HouseFlow.Application.Services;

public class AuthService : IAuthService
{
    private readonly IApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthService> _logger;

    public AuthService(IApplicationDbContext context, IConfiguration configuration, ILogger<AuthService> logger)
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>Refresh-token lifetime of a "remember me" session (sliding: renewed on every refresh).</summary>
    public static readonly TimeSpan RememberMeLifetime = TimeSpan.FromDays(365);

    /// <summary>Refresh-token lifetime of a plain session (the cookie itself dies with the browser).</summary>
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(24);

    /// <summary>Maximum concurrent sessions (token families) per user; the least recently used is evicted.</summary>
    public const int MaxSessionsPerUser = 10;

    /// <summary>
    /// Window during which an already-rotated token is still honoured: two tabs booting at the same
    /// time both send the same cookie, and the loser of that race must not be treated as a thief.
    /// </summary>
    public static readonly TimeSpan RotationGracePeriod = TimeSpan.FromSeconds(30);

    /// <summary>How long revoked/expired tokens are kept so that their reuse can still be detected.</summary>
    public static readonly TimeSpan RevokedTokenRetention = TimeSpan.FromDays(7);

    public async Task<AuthResponseDto> RegisterAsync(RegisterRequestDto request, string? ipAddress = null, string? invitationToken = null)
    {
        _logger.LogInformation("Registration attempt for email: {Email}", request.Email);

        // Check if user already exists
        if (await _context.Users.AnyAsync(u => u.Email == request.Email))
        {
            _logger.LogWarning("Registration failed - email already exists: {Email}", request.Email);
            throw new InvalidOperationException("This email address is already registered. Please use a different email or try logging in.");
        }

        // Create user
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            PasswordHash = BCryptNet.HashPassword(request.Password),
            IsAdmin = AdminBootstrap.IsBootstrapAdmin(_configuration, request.Email),
            CreatedAt = DateTime.UtcNow
        };

        // Override audit context with registration email (no JWT available for this endpoint)
        _context.SetAuditContext(null, request.Email, ipAddress);

        _context.Users.Add(user);

        // Create default first house "Ma maison"
        var house = new House
        {
            Id = Guid.NewGuid(),
            Name = "Ma maison",
            UserId = user.Id,
            CreatedAt = DateTime.UtcNow
        };

        _context.Houses.Add(house);

        // Create Owner membership for the default house
        var member = new HouseMember
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            HouseId = house.Id,
            Role = HouseRole.Owner,
            CanLogMaintenance = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.HouseMembers.Add(member);

        // Handle invitation token if provided
        if (!string.IsNullOrEmpty(invitationToken))
        {
            var invitation = await _context.Invitations
                .Include(i => i.House)
                .FirstOrDefaultAsync(i => i.Token == invitationToken
                    && i.Status == InvitationStatus.Pending
                    && i.ExpiresAt > DateTime.UtcNow);

            if (invitation != null)
            {
                var inviteMember = new HouseMember
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    HouseId = invitation.HouseId,
                    Role = invitation.Role,
                    CanLogMaintenance = true,
                    CreatedAt = DateTime.UtcNow
                };
                _context.HouseMembers.Add(inviteMember);

                invitation.Status = InvitationStatus.Accepted;
                invitation.AcceptedByUserId = user.Id;
                invitation.AcceptedAt = DateTime.UtcNow;
            }
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("User registered successfully: {UserId}, Email: {Email}", user.Id, user.Email);

        // Generate tokens (a fresh registration is never a "remember me" session)
        var refreshToken = await StartSessionAsync(user.Id, ipAddress, rememberMe: false);
        await _context.SaveChangesAsync(); // Save the refresh token

        return BuildAuthResponse(user, refreshToken);
    }

    public async Task<AuthResponseDto> LoginAsync(LoginRequestDto request, string? ipAddress = null)
    {
        _logger.LogInformation("Login attempt for email: {Email}", request.Email);

        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == request.Email);

        if (user == null)
        {
            _logger.LogWarning("Login failed - user not found: {Email}", request.Email);
            throw new UnauthorizedAccessException("Invalid email or password");
        }

        if (!BCryptNet.Verify(request.Password, user.PasswordHash))
        {
            _logger.LogWarning("Login failed - invalid password for user: {UserId}", user.Id);
            throw new UnauthorizedAccessException("Invalid email or password");
        }

        _logger.LogInformation("User logged in successfully: {UserId}", user.Id);

        // Override audit context with authenticated user (no JWT available for this endpoint)
        _context.SetAuditContext(user.Id, user.Email, ipAddress);

        // Generate tokens
        var refreshToken = await StartSessionAsync(user.Id, ipAddress, request.RememberMe ?? false);
        await _context.SaveChangesAsync(); // Save the refresh token

        return BuildAuthResponse(user, refreshToken);
    }

    public async Task<AuthResponseDto> RefreshTokenAsync(string token, string? ipAddress = null)
    {
        var refreshToken = await _context.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.Token == token);

        if (refreshToken == null)
        {
            _logger.LogWarning("Refresh token unknown");
            throw new UnauthorizedAccessException("Invalid or expired refresh token");
        }

        // Override audit context (no JWT available for this endpoint)
        _context.SetAuditContext(refreshToken.UserId, refreshToken.User?.Email, ipAddress);

        if (refreshToken.ReplacedByToken != null)
        {
            // This token has already been rotated, so two parties hold it: either a benign race
            // (two tabs refreshing with the same cookie) or a stolen cookie.
            var replacement = await _context.RefreshTokens
                .FirstOrDefaultAsync(rt => rt.Token == refreshToken.ReplacedByToken);
            var withinGrace = refreshToken.RevokedAt is { } revokedAt
                && DateTime.UtcNow - revokedAt <= RotationGracePeriod;

            if (withinGrace && replacement is { IsActive: true })
            {
                _logger.LogInformation(
                    "Rotated refresh token presented within grace period for user {UserId}; re-issuing current token",
                    refreshToken.UserId);
                return BuildAuthResponse(refreshToken.User!, replacement);
            }

            RevokeFamily(await LoadFamilyAsync(refreshToken.FamilyId), ipAddress, "Reuse detected");
            await _context.SaveChangesAsync();

            _logger.LogWarning(
                "Refresh token reuse detected for user {UserId}: family {FamilyId} revoked",
                refreshToken.UserId, refreshToken.FamilyId);
            throw new UnauthorizedAccessException("Invalid or expired refresh token");
        }

        if (!refreshToken.IsActive)
        {
            _logger.LogWarning("Refresh token revoked or expired for user {UserId}", refreshToken.UserId);
            throw new UnauthorizedAccessException("Invalid or expired refresh token");
        }

        // Replace old refresh token with new one (rotation)
        var newRefreshToken = RotateRefreshToken(refreshToken, ipAddress);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Token refreshed for user: {UserId}", refreshToken.UserId);

        return BuildAuthResponse(refreshToken.User!, newRefreshToken);
    }

    public async Task RevokeTokenAsync(string token, string? ipAddress = null)
    {
        var refreshToken = await _context.RefreshTokens.FirstOrDefaultAsync(rt => rt.Token == token);

        if (refreshToken == null || !refreshToken.IsActive)
        {
            _logger.LogWarning("Attempted to revoke invalid or expired token: {Token}", token);
            throw new InvalidOperationException("Invalid or expired token");
        }

        // Revoke token
        refreshToken.RevokedAt = DateTime.UtcNow;
        refreshToken.RevokedByIp = ipAddress;
        refreshToken.ReasonRevoked = "Revoked by user";

        await _context.SaveChangesAsync();

        _logger.LogInformation("Refresh token revoked for user: {UserId}", refreshToken.UserId);
    }

    private AuthResponseDto BuildAuthResponse(User user, RefreshToken refreshToken) => new(
        GenerateJwtToken(user.Id, user.Email, user.IsAdmin),
        refreshToken.Token,
        900, // 15 minutes
        ToUserDto(user),
        RefreshCookieExpiresAt: refreshToken.RememberMe ? refreshToken.ExpiresAt : null
    );

    /// <summary>
    /// Opens a new session (token family) for the user, evicting the least recently used
    /// sessions beyond <see cref="MaxSessionsPerUser"/> and pruning tokens past retention.
    /// </summary>
    private async Task<RefreshToken> StartSessionAsync(Guid userId, string? ipAddress, bool rememberMe)
    {
        var now = DateTime.UtcNow;
        var userTokens = await _context.RefreshTokens
            .Where(rt => rt.UserId == userId)
            .ToListAsync();

        // Housekeeping: revoked/expired tokens are only kept for reuse detection.
        var stale = userTokens.Where(rt =>
            (rt.RevokedAt != null && rt.RevokedAt < now - RevokedTokenRetention) ||
            rt.ExpiresAt < now - RevokedTokenRetention);

        // An active token's CreatedAt is its family's last rotation, i.e. last activity:
        // keep the MaxSessionsPerUser - 1 most recently used families plus the new one.
        var evictedFamilies = userTokens
            .Where(rt => rt.IsActive)
            .OrderByDescending(rt => rt.CreatedAt)
            .Skip(MaxSessionsPerUser - 1)
            .Select(rt => rt.FamilyId)
            .ToHashSet();
        var evicted = userTokens.Where(rt => evictedFamilies.Contains(rt.FamilyId));

        _context.RefreshTokens.RemoveRange(stale.Concat(evicted).Distinct());

        var refreshToken = CreateRefreshToken(userId, ipAddress, Guid.NewGuid(), rememberMe);
        _context.RefreshTokens.Add(refreshToken);
        return refreshToken;
    }

    private RefreshToken RotateRefreshToken(RefreshToken refreshToken, string? ipAddress)
    {
        // The new token stays in the same family and keeps the lifetime chosen at login (sliding expiry)
        var newRefreshToken = CreateRefreshToken(
            refreshToken.UserId, ipAddress, refreshToken.FamilyId, refreshToken.RememberMe);
        _context.RefreshTokens.Add(newRefreshToken);

        // Revoke old refresh token
        refreshToken.RevokedAt = DateTime.UtcNow;
        refreshToken.RevokedByIp = ipAddress;
        refreshToken.ReplacedByToken = newRefreshToken.Token;
        refreshToken.ReasonRevoked = "Replaced by new token";

        return newRefreshToken;
    }

    private static RefreshToken CreateRefreshToken(Guid userId, string? ipAddress, Guid familyId, bool rememberMe)
    {
        // Generate a cryptographically secure random token
        var randomBytes = new byte[64];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        var token = Convert.ToBase64String(randomBytes);

        var now = DateTime.UtcNow;
        return new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Token = token,
            FamilyId = familyId,
            RememberMe = rememberMe,
            ExpiresAt = now + (rememberMe ? RememberMeLifetime : SessionLifetime),
            CreatedAt = now,
            CreatedByIp = ipAddress
        };
    }

    private Task<List<RefreshToken>> LoadFamilyAsync(Guid familyId) =>
        _context.RefreshTokens.Where(rt => rt.FamilyId == familyId && rt.RevokedAt == null).ToListAsync();

    private static void RevokeFamily(IEnumerable<RefreshToken> activeTokens, string? ipAddress, string reason)
    {
        var now = DateTime.UtcNow;
        foreach (var token in activeTokens)
        {
            token.RevokedAt = now;
            token.RevokedByIp = ipAddress;
            token.ReasonRevoked = reason;
        }
    }

    public string GenerateJwtToken(Guid userId, string email, bool isAdmin = false)
    {
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
            _configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT Key not configured")));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        // The admin role only ever travels in JWTs (never in API-key identities), so API keys
        // can't reach the admin endpoints even when they belong to an administrator.
        if (isAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, AdminBootstrap.AdminRole));
        }

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static UserDto ToUserDto(User user) =>
        new(user.Id, user.FirstName, user.LastName, user.Email, user.Theme, user.Language, user.IsAdmin);
}
