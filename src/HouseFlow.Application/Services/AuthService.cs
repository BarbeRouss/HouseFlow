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

    /// <summary>
    /// Granularité de <see cref="User.LastLoginAt"/> sur le chemin de rafraîchissement. Un jeton
    /// d'accès vit 15 minutes : écrire l'horodatage à chaque rafraîchissement coûterait un UPDATE
    /// par quart d'heure et par utilisateur, pour servir une règle — la purge des comptes inactifs
    /// à 3 ans — qui se moque de la minute. On n'écrit donc que si la valeur stockée a vieilli
    /// d'au moins ce seuil, ce qui borne le coût à une écriture par utilisateur et par jour.
    /// </summary>
    public static readonly TimeSpan LastLoginPrecision = TimeSpan.FromHours(24);

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
        // Comparaison insensible à la casse : l'appartenance à Admin:BootstrapEmails l'est
        // (AdminBootstrap.IsBootstrapAdmin), donc une unicité sensible à la casse laisserait
        // s'inscrire une variante de casse d'une adresse d'administrateur et la ferait promouvoir.
        if (await _context.Users.AnyAsync(u => u.Email.ToLower() == request.Email.ToLower()))
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

        // Override audit context with registration email (no JWT available for this endpoint).
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

        // Generate tokens (a fresh registration is never a "remember me" session)
        var (refreshToken, plainRefreshToken) = await StartSessionAsync(user.Id, ipAddress, rememberMe: false);
        await _context.SaveChangesAsync(); // Save the refresh token

        return BuildAuthResponse(user, refreshToken, plainRefreshToken);
    }

    public async Task<AuthResponseDto> LoginAsync(LoginRequestDto request, string? ipAddress = null)
    {
        _logger.LogInformation("Login attempt");

        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == request.Email);

        if (user == null)
        {
            _logger.LogWarning("Login failed - unknown account");
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
        var (refreshToken, plainRefreshToken) = await StartSessionAsync(user.Id, ipAddress, request.RememberMe ?? false);
        await _context.SaveChangesAsync(); // Save the refresh token

        return BuildAuthResponse(user, refreshToken, plainRefreshToken);
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
            _logger.LogWarning("Refresh token unknown");
            throw new UnauthorizedAccessException("Invalid or expired refresh token");
        }

        // Override audit context (no JWT available for this endpoint)
        _context.SetAuditContext(refreshToken.UserId, refreshToken.User?.Email, ipAddress);

        // RGPD Art. 18 — contrôlé AVANT la fenêtre de grâce : placé après, un compte gelé
        // pouvait encore obtenir un jeton frère par cette porte.
        if (refreshToken.User is not null) EnsureNotRestricted(refreshToken.User);

        if (refreshToken.ReplacedByToken != null)
        {
            // This token has already been rotated, so two parties hold it: either a benign race
            // (two tabs refreshing with the same cookie) or a stolen cookie.
            var replacement = await _context.RefreshTokens
                .FirstOrDefaultAsync(rt => rt.Token == refreshToken.ReplacedByToken);
            var withinGrace = refreshToken.RevokedAt is { } revokedAt
                && DateTime.UtcNow - revokedAt <= RotationGracePeriod;

            // Une seule grâce par jeton parent. Sans cette borne, un cookie volé et rejoué en
            // boucle pendant la fenêtre frappe autant de jetons frères que voulu, et chacun
            // tourne ensuite dans sa propre chaîne : la réutilisation ne serait plus JAMAIS
            // détectée. Le deuxième rejeu retombe donc sur la révocation de famille.
            if (withinGrace && replacement is { IsActive: true } && refreshToken.GraceUsedAt is null)
            {
                // La base ne conserve que le hash : la valeur en clair du jeton courant n'est
                // pas rejouable (Art. 32(1)(a)). On délivre donc à l'onglet perdant un jeton
                // frère dans la MÊME famille, sans révoquer celui de l'onglet gagnant.
                //
                // Le frère n'ouvre pas une session neuve : il hérite de l'échéance du jeton
                // qu'il double. Un vol exploité par cette porte ne peut donc pas survivre à la
                // session légitime, là où une durée pleine lui offrirait jusqu'à un an.
                var (sibling, plainSibling) = CreateRefreshToken(
                    refreshToken.UserId, ipAddress, refreshToken.FamilyId, replacement.RememberMe);
                sibling.ExpiresAt = replacement.ExpiresAt;
                _context.RefreshTokens.Add(sibling);
                refreshToken.GraceUsedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                // Warning et non Information : c'est soit une course entre onglets, soit le
                // premier signe d'un vol de cookie. Les deux méritent d'être visibles.
                _logger.LogWarning(
                    "Rotated refresh token presented within grace period for user {UserId}; issuing sibling token in family {FamilyId}",
                    refreshToken.UserId, refreshToken.FamilyId);
                return BuildAuthResponse(refreshToken.User!, sibling, plainSibling);
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
        var (newRefreshToken, newPlainRefreshToken) = RotateRefreshToken(refreshToken, ipAddress);
        await _context.SaveChangesAsync();

        // Une session « Se souvenir de moi » est glissante sur un an : un utilisateur qui ouvre
        // l'application tous les jours sans jamais ressaisir son mot de passe ne repasse jamais
        // par LoginAsync. Sans cette mise à jour, son LastLoginAt reste figé et la purge des
        // comptes inactifs (politique de conservation § 5) le supprimerait alors qu'il est actif.
        await TouchLastLoginAsync(refreshToken.User!);

        _logger.LogInformation("Token refreshed for user: {UserId}", refreshToken.UserId);

        return BuildAuthResponse(refreshToken.User!, newRefreshToken, newPlainRefreshToken);
    }

    /// <summary>
    /// Rafraîchit <see cref="User.LastLoginAt"/> si la valeur stockée a vieilli de plus de
    /// <see cref="LastLoginPrecision"/>. L'utilisateur est déjà chargé par l'appelant : le test
    /// est une comparaison de dates en mémoire, sans aller-retour en base. L'écriture passe par
    /// <c>ExecuteUpdate</c>, donc hors change tracker : un horodatage technique n'a rien à faire
    /// dans le journal d'audit (<c>LastLoginAt</c> est de toute façon exclu de l'audit, voir
    /// <c>HouseFlowDbContext</c>) et l'UPDATE reste ciblé sur la seule colonne concernée.
    /// </summary>
    private async Task TouchLastLoginAsync(User user)
    {
        var now = DateTime.UtcNow;
        if (user.LastLoginAt is { } last && now - last < LastLoginPrecision)
            return;

        if (_context.Database.IsRelational())
        {
            // L'instance chargée n'est délibérément pas alignée : y toucher la marquerait
            // « modifiée » et le prochain SaveChanges de la requête réécrirait la colonne.
            // La valeur en mémoire n'est lue par personne d'ici la fin de la requête.
            await _context.Users.Where(u => u.Id == user.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastLoginAt, now));
        }
        else
        {
            user.LastLoginAt = now;
            await _context.SaveChangesAsync();
        }
    }

    public async Task RevokeTokenAsync(string token, string? ipAddress = null)
    {
        var tokenHash = TokenHasher.Hash(token);
        var refreshToken = await _context.RefreshTokens.FirstOrDefaultAsync(rt => rt.Token == tokenHash);

        if (refreshToken == null || !refreshToken.IsActive)
        {
            // Jamais le token lui-même dans les journaux : c'est un secret de session.
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

    /// <param name="plainRefreshToken">
    /// Valeur en clair du jeton : la base n'en détient que le hash, elle n'est donc connue
    /// qu'au moment où le jeton est créé, et ne sort d'ici que vers le cookie HttpOnly.
    /// </param>
    private AuthResponseDto BuildAuthResponse(User user, RefreshToken refreshToken, string plainRefreshToken) => new(
        GenerateJwtToken(user.Id, user.Email, user.IsAdmin),
        plainRefreshToken,
        900, // 15 minutes
        ToUserDto(user),
        RefreshCookieExpiresAt: refreshToken.RememberMe ? refreshToken.ExpiresAt : null
    );

    /// <summary>
    /// Opens a new session (token family) for the user, evicting the least recently used
    /// sessions beyond <see cref="MaxSessionsPerUser"/> and pruning tokens past retention.
    /// </summary>
    private async Task<(RefreshToken Entity, string PlainToken)> StartSessionAsync(Guid userId, string? ipAddress, bool rememberMe)
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

        var created = CreateRefreshToken(userId, ipAddress, Guid.NewGuid(), rememberMe);
        _context.RefreshTokens.Add(created.Entity);
        return created;
    }

    private (RefreshToken Entity, string PlainToken) RotateRefreshToken(RefreshToken refreshToken, string? ipAddress)
    {
        // The new token stays in the same family and keeps the lifetime chosen at login (sliding expiry)
        var (newRefreshToken, newPlainToken) = CreateRefreshToken(
            refreshToken.UserId, ipAddress, refreshToken.FamilyId, refreshToken.RememberMe);
        _context.RefreshTokens.Add(newRefreshToken);

        // Revoke old refresh token
        refreshToken.RevokedAt = DateTime.UtcNow;
        refreshToken.RevokedByIp = ipAddress;
        // Le hash, comme la colonne Token : la chaîne de rotation reste comparable sans
        // qu'aucune valeur en clair ne soit stockée.
        refreshToken.ReplacedByToken = newRefreshToken.Token;
        refreshToken.ReasonRevoked = "Replaced by new token";

        return (newRefreshToken, newPlainToken);
    }

    /// <summary>
    /// Crée un jeton de rafraîchissement. La valeur en clair n'est retournée qu'à l'appelant
    /// (elle part dans le cookie) ; la base ne reçoit que son hash SHA-256 — RGPD Art. 32(1)(a),
    /// un vol de base ne doit pas permettre de forger des sessions.
    /// </summary>
    private static (RefreshToken Entity, string PlainToken) CreateRefreshToken(Guid userId, string? ipAddress, Guid familyId, bool rememberMe)
    {
        // Generate a cryptographically secure random token
        var randomBytes = new byte[64];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        var plainToken = Convert.ToBase64String(randomBytes);

        var now = DateTime.UtcNow;
        var entity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Token = TokenHasher.Hash(plainToken),
            FamilyId = familyId,
            RememberMe = rememberMe,
            ExpiresAt = now + (rememberMe ? RememberMeLifetime : SessionLifetime),
            CreatedAt = now,
            CreatedByIp = ipAddress
        };

        return (entity, plainToken);
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

    /// <summary>
    /// Projette l'utilisateur en DTO, en signalant au frontend s'il doit (ré)accepter les CGU
    /// et la politique en vigueur (bannière de ré-acceptation).
    /// </summary>
    private static UserDto ToUserDto(User user) => new(
        user.Id, user.FirstName, user.LastName, user.Email, user.Theme, user.Language,
        GdprPolicy.IsConsentRequired(user.ConsentGivenAt, user.ConsentPolicyVersion),
        user.IsAdmin
    );
}
