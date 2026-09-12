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

    public async Task<AuthResponseDto> RegisterAsync(RegisterRequestDto request, string? ipAddress = null, string? invitationToken = null)
    {
        _logger.LogInformation("Registration attempt");

        // RGPD — l'acceptation des CGU (contrat, Art. 6(1)(b)) est une condition de conclusion
        // du contrat : refusée, aucun compte n'est créé. Vérifié AVANT toute écriture.
        if (!request.ConsentAccepted)
        {
            _logger.LogWarning("Registration failed - terms of service not accepted");
            throw new InvalidOperationException("You must accept the terms of service to create an account");
        }

        // Check if user already exists
        if (await _context.Users.AnyAsync(u => u.Email == request.Email))
        {
            _logger.LogWarning("Registration failed - email already registered");
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
            CreatedAt = DateTime.UtcNow,
            // Preuve d'accountability (Art. 5(2)) : date + version acceptée. L'IP est
            // journalisée par l'audit trail via SetAuditContext ci-dessous.
            ConsentGivenAt = DateTime.UtcNow,
            ConsentPolicyVersion = GdprPolicy.CurrentPolicyVersion
        };

        // Override audit context with registration email (no JWT available for this endpoint)
        // L'identifiant est connu avant la sauvegarde : l'attribuer dès maintenant pour que
        // toutes les entrées d'audit de l'inscription (maison par défaut, adhésion, jeton)
        // soient rattachées au compte et donc anonymisées avec lui (Art. 17).
        _context.SetAuditContext(user.Id, request.Email, ipAddress);

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

        _logger.LogInformation("User registered successfully: {UserId}", user.Id);

        // Generate tokens
        var jwtToken = GenerateJwtToken(user.Id, user.Email, user.IsAdmin);
        var (_, plainRefreshToken) = await GenerateRefreshToken(user.Id, ipAddress);
        await _context.SaveChangesAsync(); // Save the refresh token

        return new AuthResponseDto(
            jwtToken,
            plainRefreshToken,
            900, // 15 minutes
            ToUserDto(user)
        );
    }

    public async Task<AuthResponseDto> LoginAsync(LoginRequestDto request, string? ipAddress = null)
    {
        _logger.LogInformation("Login attempt");

        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == request.Email);

        if (user == null)
        {
            _logger.LogWarning("Login failed - user not found");
            throw new UnauthorizedAccessException("Invalid email or password");
        }

        if (!BCryptNet.Verify(request.Password, user.PasswordHash))
        {
            _logger.LogWarning("Login failed - invalid password for user: {UserId}", user.Id);
            throw new UnauthorizedAccessException("Invalid email or password");
        }

        EnsureNotRestricted(user);

        _logger.LogInformation("User logged in successfully: {UserId}", user.Id);

        // Override audit context with authenticated user (no JWT available for this endpoint)
        _context.SetAuditContext(user.Id, user.Email, ipAddress);

        // Dernière connexion (identification des comptes inactifs — politique de rétention).
        // ExecuteUpdate : pas d'entrée d'audit pour un simple horodatage de connexion.
        var loginAt = DateTime.UtcNow;
        if (_context.Database.IsRelational())
        {
            await _context.Users.Where(u => u.Id == user.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastLoginAt, loginAt));
        }
        else
        {
            var tracked = await _context.Users.FirstAsync(u => u.Id == user.Id);
            tracked.LastLoginAt = loginAt;
        }

        // Generate tokens
        var jwtToken = GenerateJwtToken(user.Id, user.Email, user.IsAdmin);
        var (_, plainRefreshToken) = await GenerateRefreshToken(user.Id, ipAddress);
        await _context.SaveChangesAsync(); // Save the refresh token

        return new AuthResponseDto(
            jwtToken,
            plainRefreshToken,
            900, // 15 minutes
            ToUserDto(user)
        );
    }

    public async Task<AuthResponseDto> RefreshTokenAsync(string token, string? ipAddress = null)
    {
        // RGPD Art. 32(1)(a) — la base ne contient que le hash du token ; le porteur
        // (cookie) détient la valeur en clair, le lookup se fait donc sur le hash.
        var tokenHash = TokenHasher.Hash(token);

        var refreshToken = await _context.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.Token == tokenHash);

        if (refreshToken == null)
        {
            _logger.LogWarning("Refresh token invalid or expired");
            throw new UnauthorizedAccessException("Invalid or expired refresh token");
        }

        // Détection de réutilisation (Art. 32(1)(b)) : un token déjà rotaté qui est
        // représenté signifie qu'un tiers détient une copie de la chaîne. On ne peut pas
        // distinguer le voleur du propriétaire légitime : toute la famille de tokens de
        // l'utilisateur est révoquée, ce qui force une ré-authentification par mot de passe.
        if (refreshToken.RevokedAt != null && refreshToken.ReplacedByToken != null)
        {
            // La révocation en cascade est un événement de sécurité : on l'attribue au
            // compte visé et à l'IP du présentateur du token (Art. 6(1)(f) traçabilité).
            _context.SetAuditContext(refreshToken.UserId, refreshToken.User?.Email, ipAddress);
            await RevokeAllTokensForUserAsync(refreshToken.UserId, ipAddress, "Reuse detected");
            _logger.LogWarning(
                "Refresh token reuse detected — all sessions revoked for user: {UserId}", refreshToken.UserId);
            throw new UnauthorizedAccessException("Invalid or expired refresh token");
        }

        if (!refreshToken.IsActive)
        {
            _logger.LogWarning("Refresh token invalid or expired");
            throw new UnauthorizedAccessException("Invalid or expired refresh token");
        }

        if (refreshToken.User is not null) EnsureNotRestricted(refreshToken.User);

        // Override audit context (no JWT available for this endpoint)
        _context.SetAuditContext(refreshToken.UserId, refreshToken.User?.Email, ipAddress);

        // Replace old refresh token with new one (rotation)
        var (_, newPlainRefreshToken) = await RotateRefreshToken(refreshToken, ipAddress);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Token refreshed for user: {UserId}", refreshToken.UserId);

        // Generate new JWT
        var user = refreshToken.User!;
        var jwtToken = GenerateJwtToken(user.Id, user.Email, user.IsAdmin);

        return new AuthResponseDto(
            jwtToken,
            newPlainRefreshToken,
            900, // 15 minutes
            ToUserDto(user)
        );
    }

    public async Task RevokeTokenAsync(string token, string? ipAddress = null)
    {
        var tokenHash = TokenHasher.Hash(token);
        var refreshToken = await _context.RefreshTokens.FirstOrDefaultAsync(rt => rt.Token == tokenHash);

        if (refreshToken == null || !refreshToken.IsActive)
        {
            _logger.LogWarning("Attempted to revoke invalid or expired token");
            throw new InvalidOperationException("Invalid or expired token");
        }

        // Revoke token
        refreshToken.RevokedAt = DateTime.UtcNow;
        refreshToken.RevokedByIp = ipAddress;
        refreshToken.ReasonRevoked = "Revoked by user";

        await _context.SaveChangesAsync();

        _logger.LogInformation("Refresh token revoked for user: {UserId}", refreshToken.UserId);
    }

    /// <summary>
    /// Projette l'utilisateur en DTO, en signalant au frontend s'il doit (ré)accepter les CGU
    /// et la politique en vigueur (bannière de ré-acceptation).
    /// </summary>
    /// <summary>
    /// RGPD Art. 18 — un compte sous limitation de traitement est gelé : aucune session ne
    /// peut être ouverte ni prolongée tant que la limitation n'est pas levée.
    /// </summary>
    private void EnsureNotRestricted(User user)
    {
        if (user.ProcessingRestrictedAt is null) return;
        _logger.LogWarning("Login refused - processing restricted (Art. 18) for user: {UserId}", user.Id);
        throw new UnauthorizedAccessException("This account is currently restricted. Please contact " + GdprPolicy.PrivacyContactEmail + ".");
    }

    private static UserDto ToUserDto(User user) => new(
        user.Id, user.FirstName, user.LastName, user.Email, user.Theme, user.Language,
        GdprPolicy.IsConsentRequired(user.ConsentGivenAt, user.ConsentPolicyVersion),
        user.IsAdmin
    );

    /// Crée un refresh token. La valeur en clair n'est retournée qu'à l'appelant (elle
    /// part dans le cookie) ; la base ne reçoit que son hash SHA-256
    /// (RGPD Art. 32(1)(a) — un vol de base ne doit pas permettre de forger des sessions).
    /// </summary>
    private async Task<(RefreshToken Entity, string PlainToken)> GenerateRefreshToken(Guid userId, string? ipAddress)
    {
        // Generate a cryptographically secure random token
        var randomBytes = new byte[64];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        var plainToken = Convert.ToBase64String(randomBytes);

        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Token = TokenHasher.Hash(plainToken),
            ExpiresAt = DateTime.UtcNow.AddDays(7), // 7 days
            CreatedAt = DateTime.UtcNow,
            CreatedByIp = ipAddress
        };

        // Keep at most 5 tokens per user INCLUDING the one being added: the 4 most recent
        // existing ones survive, older ones are removed.
        var oldTokens = await _context.RefreshTokens
            .Where(rt => rt.UserId == userId)
            .OrderByDescending(rt => rt.CreatedAt)
            .Skip(4)
            .ToListAsync();

        _context.RefreshTokens.RemoveRange(oldTokens);
        _context.RefreshTokens.Add(refreshToken);

        return (refreshToken, plainToken);
    }

    private async Task<(RefreshToken Entity, string PlainToken)> RotateRefreshToken(RefreshToken refreshToken, string? ipAddress)
    {
        // Generate new refresh token
        var newRefreshToken = await GenerateRefreshToken(refreshToken.UserId, ipAddress);

        // Revoke old refresh token. ReplacedByToken stores the hash too — it is both a
        // rotation chain marker and the reuse-detection signal, never a usable secret.
        refreshToken.RevokedAt = DateTime.UtcNow;
        refreshToken.RevokedByIp = ipAddress;
        refreshToken.ReplacedByToken = newRefreshToken.Entity.Token;
        refreshToken.ReasonRevoked = "Replaced by new token";

        return newRefreshToken;
    }

    /// <summary>
    /// Révoque tous les refresh tokens encore actifs d'un utilisateur. Utilisé par la
    /// détection de réutilisation : la chaîne entière tombe, pas seulement le token volé.
    /// </summary>
    private async Task RevokeAllTokensForUserAsync(Guid userId, string? ipAddress, string reason)
    {
        var now = DateTime.UtcNow;

        var activeTokens = await _context.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.RevokedAt == null)
            .ToListAsync();

        foreach (var activeToken in activeTokens)
        {
            activeToken.RevokedAt = now;
            activeToken.RevokedByIp = ipAddress;
            activeToken.ReasonRevoked = reason;
        }

        await _context.SaveChangesAsync();
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

}
