using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HouseFlow.Application.DTOs;
using HouseFlow.Application.Interfaces;
using HouseFlow.Core.Entities;
using HouseFlow.Core.Enums;
using HouseFlow.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using BCryptNet = BCrypt.Net.BCrypt;

namespace HouseFlow.Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly HouseFlowDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthService> _logger;

    public AuthService(HouseFlowDbContext context, IConfiguration configuration, ILogger<AuthService> logger)
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;
    }

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

        // Generate tokens
        var jwtToken = GenerateJwtToken(user.Id, user.Email);
        var refreshToken = await GenerateRefreshToken(user.Id, ipAddress);
        await _context.SaveChangesAsync(); // Save the refresh token

        return new AuthResponseDto(
            jwtToken,
            refreshToken.Token,
            900, // 15 minutes
            new UserDto(user.Id, user.FirstName, user.LastName, user.Email, user.Theme, user.Language)
        );
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
        var jwtToken = GenerateJwtToken(user.Id, user.Email);
        var refreshToken = await GenerateRefreshToken(user.Id, ipAddress);
        await _context.SaveChangesAsync(); // Save the refresh token

        return new AuthResponseDto(
            jwtToken,
            refreshToken.Token,
            900, // 15 minutes
            new UserDto(user.Id, user.FirstName, user.LastName, user.Email, user.Theme, user.Language)
        );
    }

    public async Task<AuthResponseDto> RefreshTokenAsync(string token, string? ipAddress = null)
    {
        var refreshToken = await _context.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.Token == token);

        if (refreshToken == null || !refreshToken.IsActive)
        {
            _logger.LogWarning("Refresh token invalid or expired: {Token}", token);
            throw new UnauthorizedAccessException("Invalid or expired refresh token");
        }

        // Override audit context (no JWT available for this endpoint)
        _context.SetAuditContext(refreshToken.UserId, refreshToken.User?.Email, ipAddress);

        // Replace old refresh token with new one (rotation)
        var newRefreshToken = await RotateRefreshToken(refreshToken, ipAddress);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Token refreshed for user: {UserId}", refreshToken.UserId);

        // Generate new JWT
        var jwtToken = GenerateJwtToken(refreshToken.UserId, refreshToken.User?.Email ?? "");

        return new AuthResponseDto(
            jwtToken,
            newRefreshToken.Token,
            900, // 15 minutes
            new UserDto(refreshToken.User!.Id, refreshToken.User.FirstName, refreshToken.User.LastName, refreshToken.User.Email, refreshToken.User.Theme, refreshToken.User.Language)
        );
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

    private async Task<RefreshToken> GenerateRefreshToken(Guid userId, string? ipAddress)
    {
        // Generate a cryptographically secure random token
        var randomBytes = new byte[64];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        var token = Convert.ToBase64String(randomBytes);

        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Token = token,
            ExpiresAt = DateTime.UtcNow.AddDays(7), // 7 days
            CreatedAt = DateTime.UtcNow,
            CreatedByIp = ipAddress
        };

        // Remove old refresh tokens for this user (keep only last 5)
        var oldTokens = await _context.RefreshTokens
            .Where(rt => rt.UserId == userId)
            .OrderByDescending(rt => rt.CreatedAt)
            .Skip(5)
            .ToListAsync();

        _context.RefreshTokens.RemoveRange(oldTokens);
        _context.RefreshTokens.Add(refreshToken);

        return refreshToken;
    }

    private async Task<RefreshToken> RotateRefreshToken(RefreshToken refreshToken, string? ipAddress)
    {
        // Generate new refresh token
        var newRefreshToken = await GenerateRefreshToken(refreshToken.UserId, ipAddress);

        // Revoke old refresh token
        refreshToken.RevokedAt = DateTime.UtcNow;
        refreshToken.RevokedByIp = ipAddress;
        refreshToken.ReplacedByToken = newRefreshToken.Token;
        refreshToken.ReasonRevoked = "Replaced by new token";

        return newRefreshToken;
    }

    public string GenerateJwtToken(Guid userId, string email)
    {
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
            _configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT Key not configured")));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
