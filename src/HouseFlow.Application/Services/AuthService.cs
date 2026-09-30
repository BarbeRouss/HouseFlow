using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
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

        // Forme canonique (EmailNormalizer) : stockée telle quelle, elle rend la connexion et
        // l'unicité insensibles à la casse sans fonction SQL sur la colonne. L'unicité insensible à
        // la casse compte aussi pour les administrateurs : l'appartenance à Admin:BootstrapEmails
        // l'est (AdminBootstrap.IsBootstrapAdmin), une variante de casse d'une adresse
        // d'administrateur ne doit donc pas pouvoir s'inscrire et être promue.
        var email = EmailNormalizer.Normalize(request.Email);

        if (await _context.Users.AnyAsync(u => u.Email == email))
        {
            _logger.LogWarning("Registration failed - email already registered");
            throw new ConflictException(ErrorCodes.EmailTaken,
                "This email address is already registered. Please use a different email or try logging in.");
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            // Hors transaction : BCrypt coûte ~100 ms, inutile de garder la transaction ouverte pendant ce temps.
            PasswordHash = BCryptNet.HashPassword(request.Password),
            IsAdmin = AdminBootstrap.IsBootstrapAdmin(_configuration, email),
            CreatedAt = DateTime.UtcNow,
            // Preuve d'accountability (Art. 5(2)) : date + version acceptée. L'IP est
            // journalisée par l'audit trail via SetAuditContext ci-dessous.
            ConsentGivenAt = DateTime.UtcNow,
            ConsentPolicyVersion = GdprPolicy.CurrentPolicyVersion
        };

        // P03 with ?invitation= : the invitation is validated BEFORE anything is written, so a bad
        // link creates no account, and — like AcceptInvitationAsync — inside a Serializable
        // transaction: the invitation stays single-use even if an existing account accepts the
        // same link at the same moment (both would otherwise read it Pending and both join).
        Guid? joinedHouseId = null;
        await InSerializableTransactionAsync(!string.IsNullOrEmpty(invitationToken), async () =>
        {
            Invitation? invitation = null;
            if (!string.IsNullOrEmpty(invitationToken))
                invitation = await LoadInvitationForRegistrationAsync(invitationToken, email);

            // Override audit context with registration email (no JWT available for this endpoint).
            // L'identifiant est connu avant la sauvegarde : l'attribuer dès maintenant pour que
            // toutes les entrées d'audit de l'inscription (adhésion, jeton) soient rattachées au
            // compte et donc anonymisées avec lui (Art. 17).
            _context.SetAuditContext(user.Id, email, ipAddress);

            _context.Users.Add(user);

            // No house is created here any more: the first house comes from onboarding (P05,
            // POST /houses). An invited user only gets the shared house.
            if (invitation != null)
            {
                _context.HouseMembers.Add(HouseMemberService.NewMembership(invitation, user.Id));
                HouseMemberService.MarkAccepted(invitation, user.Id);
                joinedHouseId = invitation.HouseId;
            }

            await _context.SaveChangesAsync();
        });

        _logger.LogInformation("User registered successfully: {UserId}", user.Id);

        // Generate tokens (a fresh registration is never a "remember me" session)
        var (refreshToken, plainRefreshToken) = await StartSessionAsync(user.Id, ipAddress, rememberMe: false);
        await _context.SaveChangesAsync(); // Save the refresh token

        return BuildAuthResponse(user, refreshToken, plainRefreshToken) with { JoinedHouseId = joinedHouseId };
    }

    /// <summary>The registration's invitation, refused (no account created) unless usable and addressed to <paramref name="email"/>.</summary>
    private async Task<Invitation> LoadInvitationForRegistrationAsync(string invitationToken, string email)
    {
        var invitation = await _context.Invitations
            .Include(i => i.House)
            .FirstOrDefaultAsync(i => i.Token == invitationToken);

        if (invitation == null || !HouseMemberService.IsUsable(invitation, DateTime.UtcNow))
        {
            _logger.LogWarning("Registration failed - invitation unknown or no longer valid");
            throw new BusinessRuleException(ErrorCodes.InvitationInvalid, "This invitation is no longer valid");
        }

        // The invitation email locks the registration email (invitations predating the email
        // field carry none and are not checked).
        if (!HouseMemberService.IsInvitee(invitation, email))
        {
            _logger.LogWarning("Registration failed - email does not match the invitation");
            throw new BusinessRuleException(ErrorCodes.InvitationEmailMismatch,
                "The email must be the one the invitation was sent to");
        }

        return invitation;
    }

    /// <summary>
    /// Runs <paramref name="work"/> in a Serializable transaction when <paramref name="serializable"/>
    /// is set and the provider is relational, through the execution strategy: a Postgres 40001
    /// serialization failure re-runs the whole delegate, on a cleared change tracker so that the
    /// retry re-reads the database instead of the rolled-back attempt's tracked entities. The
    /// in-memory provider of the unit tests has no transactions: the work simply runs.
    /// </summary>
    private async Task InSerializableTransactionAsync(bool serializable, Func<Task> work)
    {
        if (!serializable || !_context.Database.IsRelational())
        {
            await work();
            return;
        }

        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            _context.ChangeTracker.Clear();
            await using var transaction = await _context.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable);
            await work();
            await transaction.CommitAsync();
        });
    }

    public async Task<AuthResponseDto> LoginAsync(LoginRequestDto request, string? ipAddress = null)
    {
        _logger.LogInformation("Login attempt");

        // Stored emails are canonical (EmailNormalizer): the lookup ignores case and surrounding
        // spaces while staying an exact match on the unique index.
        var email = EmailNormalizer.Normalize(request.Email);
        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email);

        if (user == null)
        {
            _logger.LogWarning("Login failed - unknown account");
            throw new AuthenticationFailedException(ErrorCodes.InvalidCredentials, "Invalid email or password");
        }

        if (!BCryptNet.Verify(request.Password, user.PasswordHash))
        {
            _logger.LogWarning("Login failed - invalid password for user: {UserId}", user.Id);
            throw new AuthenticationFailedException(ErrorCodes.InvalidCredentials, "Invalid email or password");
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

    public Task<AuthResponseDto> RefreshTokenAsync(string token, string? ipAddress = null) =>
        RefreshTokenCoreAsync(token, ipAddress, retryOnLostRotationRace: true);

    private async Task<AuthResponseDto> RefreshTokenCoreAsync(string token, string? ipAddress, bool retryOnLostRotationRace)
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
            throw new AuthenticationFailedException(ErrorCodes.InvalidRefreshToken, "Invalid or expired refresh token");
        }

        // Override audit context (no JWT available for this endpoint)
        _context.SetAuditContext(refreshToken.UserId, refreshToken.User?.Email, ipAddress);

        // RGPD Art. 18 — contrôlé AVANT la fenêtre de grâce : placé après, un compte gelé
        // pouvait encore obtenir un jeton frère par cette porte.
        if (refreshToken.User is not null) EnsureNotRestricted(refreshToken.User);

        if (refreshToken.ReplacedByToken != null)
        {
            // This token has already been rotated, so two parties hold it: either a benign race
            // (two tabs refreshing with the same cookie, or a refresh response lost to a page
            // reload) or a stolen cookie.
            var replacement = await _context.RefreshTokens
                .FirstOrDefaultAsync(rt => rt.Token == refreshToken.ReplacedByToken);
            var withinGrace = refreshToken.RevokedAt is { } revokedAt
                && DateTime.UtcNow - revokedAt <= RotationGracePeriod;

            if (withinGrace && replacement is { IsActive: true })
            {
                var grace = await GetOrCreateGraceSiblingAsync(refreshToken, token, replacement, ipAddress);
                if (grace is { } g)
                {
                    // Warning et non Information : c'est soit une course entre onglets (ou une
                    // réponse perdue), soit le premier signe d'un vol de cookie. Les deux
                    // méritent d'être visibles.
                    _logger.LogWarning(
                        "Rotated refresh token presented within grace period for user {UserId}; issuing sibling token in family {FamilyId}",
                        refreshToken.UserId, refreshToken.FamilyId);
                    return BuildAuthResponse(refreshToken.User!, g.Entity, g.PlainToken);
                }
            }

            await RevokeFamilyAsync(refreshToken.FamilyId, ipAddress, "Reuse detected");

            _logger.LogWarning(
                "Refresh token reuse detected for user {UserId}: family {FamilyId} revoked",
                refreshToken.UserId, refreshToken.FamilyId);
            throw new AuthenticationFailedException(ErrorCodes.InvalidRefreshToken, "Invalid or expired refresh token");
        }

        if (!refreshToken.IsActive)
        {
            _logger.LogWarning("Refresh token revoked or expired for user {UserId}", refreshToken.UserId);
            throw new AuthenticationFailedException(ErrorCodes.InvalidRefreshToken, "Invalid or expired refresh token");
        }

        // Replace old refresh token with new one (rotation)
        var (newRefreshToken, newPlainRefreshToken) = RotateRefreshToken(refreshToken, ipAddress);
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException) when (retryOnLostRotationRace)
        {
            // Two requests presented the same active token at the same instant (two tabs booting,
            // or a thief racing the victim). The token's xmin concurrency token lets only one of
            // them rotate it — without it both succeeded and two independent chains lived on in
            // the family, out of reach of reuse detection. The loser re-reads the token, now
            // rotated a few milliseconds ago, and goes through the grace path like any late
            // duplicate: it receives the family's single sibling.
            _context.ChangeTracker.Clear();
            return await RefreshTokenCoreAsync(token, ipAddress, retryOnLostRotationRace: false);
        }

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
        var refreshToken = await _context.RefreshTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(rt => rt.Token == tokenHash);

        // The whole family, not just the presented token: a session is the family. Revoking the
        // cookie's token alone left the family's other live tokens working after « Se déconnecter »
        // — the grace sibling (held by a thief, or by the loser of a race) and the replacement
        // whose refresh response was lost (an orphan nobody should hold, alive up to 365 days).
        // A cookie already rotated by such a lost response still ends its session this way.
        var revoked = refreshToken == null
            ? 0
            : await RevokeFamilyAsync(refreshToken.FamilyId, ipAddress, "Revoked by user");

        if (revoked == 0)
        {
            // Jamais le token lui-même dans les journaux : c'est un secret de session.
            _logger.LogWarning("Attempted to revoke invalid or expired token");
            throw new InvalidOperationException("Invalid or expired token");
        }

        _logger.LogInformation("Session revoked for user {UserId}: family {FamilyId}", refreshToken!.UserId, refreshToken.FamilyId);
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
    private static (RefreshToken Entity, string PlainToken) CreateRefreshToken(
        Guid userId, string? ipAddress, Guid familyId, bool rememberMe, string? plainToken = null)
    {
        // Generate a cryptographically secure random token (unless the caller derived one)
        plainToken ??= Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

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

    /// <summary>
    /// Fenêtre de grâce : délivre le jeton frère d'un jeton déjà rotaté, ou <c>null</c> si ce
    /// rejeu doit être traité comme une réutilisation.
    /// <para>
    /// La base ne conserve que le hash : la valeur en clair du jeton courant n'est pas
    /// rejouable (Art. 32(1)(a)). On délivre donc au perdant un jeton frère dans la MÊME
    /// famille, sans révoquer le remplaçant. Le frère reprend l'échéance du remplaçant qu'il
    /// double, mais seulement jusqu'à sa première rotation : comme tout jeton de la famille, son
    /// successeur repart sur une durée pleine (échéance glissante, 365 j ou 24 h). Ce qui borne
    /// un vol passé par cette porte n'est donc pas une échéance, c'est la détection : dès que
    /// les deux détenteurs font tourner le même frère, le second retombe sur la révocation de
    /// famille — et une déconnexion révoque de toute façon la famille entière, frère compris.
    /// </para>
    /// <para>
    /// Un seul frère par jeton parent, et il est <b>déterministe</b> (dérivé du parent par
    /// HMAC sous une clé serveur) : tout rejeu du parent pendant la fenêtre reçoit le MÊME
    /// frère tant que personne ne l'a encore utilisé. Deux cas le justifient :
    /// </para>
    /// <list type="bullet">
    /// <item>la réponse d'un rafraîchissement peut être perdue alors que le serveur a déjà
    /// tourné le jeton — rechargement de la page pendant le démarrage, onglet fermé, réseau
    /// mobile qui décroche. Le navigateur garde alors l'ancien cookie ; deux pertes de suite
    /// suffisaient à révoquer la famille et à déconnecter un utilisateur légitime ;</item>
    /// <item>un cookie volé rejoué en boucle pendant la fenêtre ne peut toujours pas ouvrir de
    /// chaînes parallèles : il obtient le même frère que la victime, et dès que l'un des deux
    /// l'a tourné, le rejeu suivant retombe sur la révocation de famille.</item>
    /// </list>
    /// </summary>
    private async Task<(RefreshToken Entity, string PlainToken)?> GetOrCreateGraceSiblingAsync(
        RefreshToken parent, string parentPlainToken, RefreshToken replacement, string? ipAddress)
    {
        var plainSibling = DeriveGraceToken(parentPlainToken);

        if (parent.GraceUsedAt is not null)
            return await FindUnusedSiblingAsync(plainSibling);

        var (sibling, _) = CreateRefreshToken(
            parent.UserId, ipAddress, parent.FamilyId, replacement.RememberMe, plainSibling);
        sibling.ExpiresAt = replacement.ExpiresAt;
        _context.RefreshTokens.Add(sibling);
        parent.GraceUsedAt = DateTime.UtcNow;
        try
        {
            await _context.SaveChangesAsync();
            return (sibling, plainSibling);
        }
        catch (DbUpdateException ex) when (IsLostSiblingRace(ex))
        {
            // Deux rejeux simultanés du même parent : l'autre requête a inséré ce même frère
            // (index unique sur Token) ou marqué le parent avant nous (xmin). Le résultat est
            // identique, on le relit. Toute autre erreur de base remonte (5xx) : elle ne doit pas
            // déconnecter l'utilisateur en passant pour un rejeu.
            _context.ChangeTracker.Clear();
            return await FindUnusedSiblingAsync(plainSibling);
        }
    }

    /// <summary>
    /// The other request won the race: a unique violation (Postgres SQLSTATE 23505, read through
    /// <see cref="System.Data.Common.DbException.SqlState"/> so the Application layer needs no
    /// provider reference) or the parent's concurrency token changed underneath us.
    /// </summary>
    private static bool IsLostSiblingRace(DbUpdateException ex) =>
        ex is DbUpdateConcurrencyException
        || ex.InnerException is System.Data.Common.DbException { SqlState: "23505" };

    /// <summary>Le frère déjà délivré, s'il n'a encore été ni tourné ni révoqué.</summary>
    private async Task<(RefreshToken Entity, string PlainToken)?> FindUnusedSiblingAsync(string plainSibling)
    {
        var hash = TokenHasher.Hash(plainSibling);
        var sibling = await _context.RefreshTokens.FirstOrDefaultAsync(rt => rt.Token == hash);
        return sibling is { IsActive: true } ? (sibling, plainSibling) : null;
    }

    /// <summary>
    /// Jeton frère d'un parent : HMAC-SHA512 de sa valeur en clair, sous une sous-clé dérivée
    /// (HKDF) de <c>Jwt:Key</c>. Sans la clé serveur, ni le détenteur du parent ni un vol de
    /// la base (qui ne contient que des hash) ne peut calculer le frère hors ligne.
    /// </summary>
    private string DeriveGraceToken(string parentPlainToken)
    {
        var secret = Encoding.UTF8.GetBytes(
            _configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT Key not configured"));
        var key = HKDF.DeriveKey(HashAlgorithmName.SHA512, secret, 64,
            info: Encoding.UTF8.GetBytes("houseflow/refresh-token/grace-sibling"));
        return Convert.ToBase64String(HMACSHA512.HashData(key, Encoding.UTF8.GetBytes(parentPlainToken)));
    }

    /// <summary>
    /// Revokes every still-active token of a family (one session) and returns how many were.
    /// <para>
    /// A single set-based <c>UPDATE … WHERE FamilyId = @f AND RevokedAt IS NULL</c>: it also
    /// catches a token a concurrent rotation inserted a moment ago, and it cannot fail on the
    /// xmin concurrency token the way per-row tracked updates would while another request of the
    /// same family is rotating. Like the mass revocation (<c>--revoke-all-sessions</c>), it
    /// bypasses the change tracker, so no audit entry is written per token — the revocation
    /// itself is logged, and the tokens keep <c>RevokedAt</c> / <c>ReasonRevoked</c>.
    /// </para>
    /// </summary>
    private async Task<int> RevokeFamilyAsync(Guid familyId, string? ipAddress, string reason)
    {
        var now = DateTime.UtcNow;
        var active = _context.RefreshTokens.Where(rt => rt.FamilyId == familyId && rt.RevokedAt == null);

        if (_context.Database.IsRelational())
        {
            return await active.ExecuteUpdateAsync(s => s
                .SetProperty(rt => rt.RevokedAt, now)
                .SetProperty(rt => rt.RevokedByIp, ipAddress)
                .SetProperty(rt => rt.ReasonRevoked, reason));
        }

        // In-memory provider (unit tests): no ExecuteUpdate.
        var tokens = await active.ToListAsync();
        foreach (var token in tokens)
        {
            token.RevokedAt = now;
            token.RevokedByIp = ipAddress;
            token.ReasonRevoked = reason;
        }
        await _context.SaveChangesAsync();
        return tokens.Count;
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
        throw new AuthenticationFailedException(ErrorCodes.AccountRestricted,
            "This account is currently restricted. Please contact " + GdprPolicy.PrivacyContactEmail + ".");
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
