using HouseFlow.Application.Common;
using HouseFlow.Application.DTOs;
using HouseFlow.Application.Interfaces;
using HouseFlow.Core.Entities;
using HouseFlow.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using BCryptNet = BCrypt.Net.BCrypt;

namespace HouseFlow.Application.Services;

/// <summary>
/// Exercice en self-service des droits de la personne concernée (RGPD Art. 15, 16, 17, 20).
/// Automatiser ces droits dans l'application est, pour la CNIL, la meilleure preuve de
/// conformité : la demande est traitée immédiatement, gratuitement et sans vérification
/// d'identité supplémentaire (l'utilisateur authentifié est réputé identifié, Art. 12).
/// </summary>
public class UserAccountService : IUserAccountService
{
    /// <summary>Un seul export par heure et par utilisateur (Art. 12(5) — demandes excessives).</summary>
    public static readonly TimeSpan ExportCooldown = TimeSpan.FromHours(1);

    internal const string DataExportAction = "DataExport";
    internal const string AccountDeletedAction = "AccountDeleted";
    /// <summary>Valeur substituée à l'UUID d'un compte supprimé dans les journaux d'audit.</summary>
    internal const string DeletedEntityId = "deleted";
    private const string UserEntityType = "User";

    private readonly IApplicationDbContext _context;
    private readonly IMaintenanceCalculatorService _calculator;
    private readonly ILogger<UserAccountService> _logger;

    public UserAccountService(
        IApplicationDbContext context,
        IMaintenanceCalculatorService calculator,
        ILogger<UserAccountService> logger)
    {
        _context = context;
        _calculator = calculator;
        _logger = logger;
    }

    // ========================================================================
    // Art. 15 — profil
    // ========================================================================

    public async Task<UserProfileDto> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new KeyNotFoundException("User not found");

        return ToProfile(user);
    }

    // ========================================================================
    // Art. 16 — rectification
    // ========================================================================

    public async Task<UserProfileDto> UpdateProfileAsync(Guid userId, UpdateProfileRequestDto request, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new KeyNotFoundException("User not found");

        var firstName = request.FirstName.Trim();
        var lastName = request.LastName.Trim();
        var email = request.Email.Trim();

        if (firstName.Length == 0 || lastName.Length == 0 || email.Length == 0)
        {
            throw new InvalidOperationException("First name, last name and email are required");
        }

        if (!string.Equals(email, user.Email, StringComparison.OrdinalIgnoreCase))
        {
            var taken = await _context.Users
                .AnyAsync(u => u.Id != userId && u.Email == email, cancellationToken);

            if (taken)
            {
                throw new InvalidOperationException("This email address is already used");
            }
        }

        user.FirstName = firstName;
        user.LastName = lastName;
        user.Email = email;
        user.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        // Le JWT en cours contient l'ancien email dans ses claims : sans importance,
        // l'identité repose sur le claim `sub` (id). Le frontend rafraîchit sa copie locale.
        return ToProfile(user);
    }

    // ========================================================================
    // Art. 17 — effacement
    // ========================================================================

    public async Task DeleteAccountAsync(Guid userId, string password, string? ipAddress = null, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new KeyNotFoundException("User not found");

        // Ressaisie du mot de passe : protège contre la suppression accidentelle et
        // contre la suppression malveillante depuis une session volée (Art. 32).
        if (!BCryptNet.Verify(password, user.PasswordHash))
        {
            _logger.LogWarning("Account deletion refused: password confirmation failed");
            throw new InvalidOperationException("Invalid password");
        }

        // La suppression doit être atomique : soit tout part, soit rien. La transaction
        // doit passer par la stratégie d'exécution (Npgsql réessaie les erreurs
        // transitoires et refuse une transaction ouverte hors de son contrôle) ; sur
        // l'InMemory provider des tests unitaires, les transactions ne sont pas gérées.
        var strategy = _context.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            var transaction = _context.Database.IsRelational()
                ? await _context.Database.BeginTransactionAsync(cancellationToken)
                : null;

            try
            {
                await DeleteOwnedHousesOrTransferAsync(userId, cancellationToken);
                await DeleteRemainingMembershipsAsync(userId, cancellationToken);
                await DeleteInvitationsAsync(userId, cancellationToken);
                await DeleteCredentialsAsync(userId, cancellationToken);

                _context.Users.Remove(user);

                // Cette sauvegarde génère elle-même des entrées d'audit nominatives
                // (suppressions) : elles sont anonymisées juste après.
                await _context.SaveChangesAsync(cancellationToken);

                await AnonymizeAuditTrailAsync(userId, user.Email, cancellationToken);

                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
            }
            catch
            {
                if (transaction is not null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                }
                throw;
            }
            finally
            {
                transaction?.Dispose();
            }
        });

        _logger.LogInformation("Account deleted (GDPR Art. 17) and audit trail anonymized");
    }

    /// <summary>
    /// Les maisons partagées appartiennent aussi aux membres restants : la propriété est
    /// transférée au collaborateur le plus ancien (RW d'abord, puis RO) plutôt que de
    /// détruire leurs données. Sans collaborateur (maison solo, ou uniquement des
    /// locataires), la maison et son contenu sont supprimés.
    /// </summary>
    private async Task DeleteOwnedHousesOrTransferAsync(Guid userId, CancellationToken cancellationToken)
    {
        var ownedHouses = await _context.Houses
            .Where(h => h.UserId == userId)
            .Include(h => h.Members)
            .Include(h => h.Invitations)
            .Include(h => h.Devices)
                .ThenInclude(d => d.MaintenanceTypes)
                    .ThenInclude(t => t.MaintenanceInstances)
            .ToListAsync(cancellationToken);

        foreach (var house in ownedHouses)
        {
            var others = house.Members.Where(m => m.UserId != userId).ToList();

            var successor = others
                    .Where(m => m.Role == HouseRole.CollaboratorRW)
                    .OrderBy(m => m.CreatedAt)
                    .FirstOrDefault()
                ?? others
                    .Where(m => m.Role == HouseRole.CollaboratorRO)
                    .OrderBy(m => m.CreatedAt)
                    .FirstOrDefault();

            if (successor is not null)
            {
                house.UserId = successor.UserId;
                successor.Role = HouseRole.Owner;
                successor.UpdatedAt = DateTime.UtcNow;

                foreach (var own in house.Members.Where(m => m.UserId == userId).ToList())
                {
                    _context.HouseMembers.Remove(own);
                }
                continue;
            }

            // Suppression explicite des enfants : la cascade DB s'en chargerait en
            // relationnel, mais l'InMemory provider (tests unitaires) ne la simule pas.
            foreach (var device in house.Devices)
            {
                foreach (var type in device.MaintenanceTypes)
                {
                    _context.MaintenanceInstances.RemoveRange(type.MaintenanceInstances);
                }
                _context.MaintenanceTypes.RemoveRange(device.MaintenanceTypes);
            }

            _context.Devices.RemoveRange(house.Devices);
            _context.Invitations.RemoveRange(house.Invitations);
            _context.HouseMembers.RemoveRange(house.Members);
            _context.Houses.Remove(house);
        }
    }

    /// <summary>Adhésions aux maisons d'autrui : seul le membre est retiré, la maison est intacte.</summary>
    private async Task DeleteRemainingMembershipsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var memberships = await _context.HouseMembers
            .Where(m => m.UserId == userId)
            .ToListAsync(cancellationToken);

        _context.HouseMembers.RemoveRange(memberships);
    }

    private async Task DeleteInvitationsAsync(Guid userId, CancellationToken cancellationToken)
    {
        // FK Restrict : les invitations émises doivent partir avant l'utilisateur.
        var sent = await _context.Invitations
            .Where(i => i.CreatedByUserId == userId)
            .ToListAsync(cancellationToken);

        _context.Invitations.RemoveRange(sent);

        // FK SetNull côté base ; posé explicitement pour l'InMemory provider.
        var received = await _context.Invitations
            .Where(i => i.AcceptedByUserId == userId)
            .ToListAsync(cancellationToken);

        foreach (var invitation in received)
        {
            invitation.AcceptedByUserId = null;
        }
    }

    /// <summary>Révocation immédiate de toutes les sessions et clés API (Art. 17 + 32).</summary>
    private async Task DeleteCredentialsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var tokens = await _context.RefreshTokens
            .Where(t => t.UserId == userId)
            .ToListAsync(cancellationToken);

        _context.RefreshTokens.RemoveRange(tokens);

        var apiKeys = await _context.ApiKeys
            .Where(k => k.UserId == userId)
            .ToListAsync(cancellationToken);

        _context.ApiKeys.RemoveRange(apiKeys);
    }

    /// <summary>
    /// Les journaux d'audit sont conservés au titre de l'intérêt légitime de sécurité
    /// (Art. 6(1)(f)), mais rendus anonymes : le lien avec l'identifiant utilisateur est
    /// rompu, le nom d'utilisateur, l'IP, l'user agent et les valeurs avant/après sont
    /// supprimés. Sans individualisation, corrélation ni inférence possibles, ces
    /// enregistrements sortent du champ du RGPD (considérant 26, avis WP216).
    /// </summary>
    private async Task AnonymizeAuditTrailAsync(Guid userId, string email, CancellationToken cancellationToken)
    {
        var entityId = userId.ToString();

        // Trois critères (défense en profondeur) : l'identifiant, l'email utilisé comme
        // « username » d'audit (entrées écrites avant que l'identifiant ne soit connu, ou
        // par des versions antérieures du code), et les entrées portant sur l'entité User.
        var logs = await _context.AuditLogs
            .Where(a => a.UserId == userId
                     || a.Username == email
                     || (a.EntityType == UserEntityType && a.EntityId == entityId))
            .ToListAsync(cancellationToken);

        foreach (var log in logs)
        {
            log.UserId = null;
            log.Username = GdprPolicy.DeletedUserName;
            log.IpAddress = null;
            log.UserAgent = null;
            log.OldValues = null;
            log.NewValues = null;
            log.ChangedProperties = null;
            log.AdditionalData = null;
            // L'UUID du compte supprimé n'est plus rattachable à personne, mais il permettrait
            // encore d'individualiser un ensemble d'entrées (WP216) : on le remplace.
            if (log.EntityType == UserEntityType && log.EntityId == entityId)
                log.EntityId = DeletedEntityId;
        }

        // Traçabilité réglementaire de la suppression elle-même — sans aucune donnée
        // identifiante (ni IP, ni user agent, ni email, ni identifiant du compte).
        _context.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            EntityType = UserEntityType,
            EntityId = DeletedEntityId,
            Action = AccountDeletedAction,
            UserId = null,
            Username = GdprPolicy.DeletedUserName,
            Timestamp = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(cancellationToken);
    }

    // ========================================================================
    // Art. 15 + 20 — export
    // ========================================================================

    public async Task<UserDataExportDto> ExportDataAsync(Guid userId, string? ipAddress = null, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new KeyNotFoundException("User not found");

        await EnsureExportQuotaAvailableAsync(userId, cancellationToken);

        var houses = await _context.Houses
            .AsNoTracking()
            .Where(h => h.UserId == userId)
            .Include(h => h.Devices)
                .ThenInclude(d => d.MaintenanceTypes)
                    .ThenInclude(t => t.MaintenanceInstances)
            .OrderBy(h => h.CreatedAt)
            .ToListAsync(cancellationToken);

        var memberships = await _context.HouseMembers
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.House!.UserId != userId)
            .Include(m => m.House)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken);

        var invitationsSent = await _context.Invitations
            .AsNoTracking()
            .Where(i => i.CreatedByUserId == userId)
            .Include(i => i.House)
            .OrderBy(i => i.CreatedAt)
            .ToListAsync(cancellationToken);

        var invitationsReceived = await _context.Invitations
            .AsNoTracking()
            .Where(i => i.AcceptedByUserId == userId)
            .Include(i => i.House)
            .OrderBy(i => i.AcceptedAt)
            .ToListAsync(cancellationToken);

        var apiKeys = await _context.ApiKeys
            .AsNoTracking()
            .Where(k => k.UserId == userId)
            .OrderBy(k => k.CreatedAt)
            .ToListAsync(cancellationToken);

        var sessions = await _context.RefreshTokens
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(cancellationToken);

        var auditLogs = await _context.AuditLogs
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderBy(a => a.Timestamp)
            .ToListAsync(cancellationToken);

        var export = new UserDataExportDto(
            ExportedAt: DateTime.UtcNow,
            FormatVersion: "1.0",
            Profile: new ExportProfileDto(user.Id, user.Email, user.FirstName, user.LastName, user.CreatedAt, user.UpdatedAt, user.LastLoginAt),
            Preferences: new ExportPreferencesDto(user.Theme, user.Language),
            Consent: new ExportConsentDto(user.ConsentGivenAt, user.ConsentPolicyVersion),
            Houses: houses.Select(ToExportHouse).ToList(),
            Memberships: memberships.Select(m => new ExportMembershipDto(
                m.HouseId,
                m.House?.Name ?? string.Empty,
                m.Role.ToString(),
                m.CanLogMaintenance,
                m.CanViewCosts,
                m.CreatedAt)).ToList(),
            InvitationsSent: invitationsSent.Select(i => new ExportInvitationSentDto(
                i.Id,
                i.House?.Name ?? string.Empty,
                i.Role.ToString(),
                i.Status.ToString(),
                i.CreatedAt,
                i.ExpiresAt,
                i.AcceptedAt)).ToList(),
            InvitationsReceived: invitationsReceived.Select(i => new ExportInvitationReceivedDto(
                i.Id,
                i.House?.Name ?? string.Empty,
                i.Role.ToString(),
                i.AcceptedAt)).ToList(),
            ApiKeys: apiKeys.Select(k => new ExportApiKeyDto(
                k.Name, k.Prefix, k.Scope.ToString(), k.CreatedAt, k.LastUsedAt, k.RevokedAt)).ToList(),
            Sessions: sessions.Select(t => new ExportSessionDto(
                t.CreatedAt, t.ExpiresAt, t.CreatedByIp, t.RevokedAt, t.RevokedByIp, t.ReasonRevoked)).ToList(),
            AuditLogs: auditLogs.Select(a => new ExportAuditLogDto(
                a.Timestamp, a.Action, a.EntityType, a.EntityId, a.ChangedProperties, a.IpAddress, a.UserAgent)).ToList(),
            Information: BuildInformation());

        // Le quota n'est consommé qu'une fois l'export effectivement produit (Art. 12(5) :
        // un échec technique ne doit pas priver l'utilisateur de l'exercice de son droit).
        await RecordExportAsync(userId, user.Email, ipAddress, cancellationToken);

        return export;
    }

    /// <summary>
    /// Art. 12(5) : la réponse est gratuite, mais des demandes répétitives peuvent être
    /// encadrées. Un export par heure, journalisé (Art. 5(2) — accountability).
    /// </summary>
    private async Task EnsureExportQuotaAvailableAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var lastExport = await _context.AuditLogs
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.Action == DataExportAction)
            .OrderByDescending(a => a.Timestamp)
            .FirstOrDefaultAsync(cancellationToken);

        if (lastExport is null) return;

        var elapsed = now - lastExport.Timestamp;
        if (elapsed < ExportCooldown)
        {
            var retryAfter = (int)Math.Ceiling((ExportCooldown - elapsed).TotalSeconds);
            throw new TooManyRequestsException(retryAfter,
                "A data export was already produced less than an hour ago. Please try again later.");
        }
    }

    private async Task RecordExportAsync(Guid userId, string email, string? ipAddress, CancellationToken cancellationToken)
    {
        _context.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            EntityType = UserEntityType,
            EntityId = userId.ToString(),
            Action = DataExportAction,
            UserId = userId,
            Username = email,
            IpAddress = ipAddress,
            Timestamp = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(cancellationToken);
    }

    private ExportHouseDto ToExportHouse(House house) => new(
        house.Id,
        house.Name,
        house.Address,
        house.ZipCode,
        house.City,
        house.Country,
        house.CreatedAt,
        house.Devices
            .OrderBy(d => d.CreatedAt)
            .Select(d => new ExportDeviceDto(
                d.Id,
                d.Name,
                d.Type,
                d.Brand,
                d.Model,
                d.InstallDate,
                d.CreatedAt,
                d.MaintenanceTypes
                    .OrderBy(t => t.CreatedAt)
                    .Select(t => new ExportMaintenanceTypeDto(
                        t.Id,
                        t.Name,
                        t.Periodicity.ToString(),
                        t.CustomDays,
                        _calculator.CalculateMaintenanceTypeStatus(t, DateTime.UtcNow.Date),
                        t.MaintenanceInstances
                            .OrderBy(i => i.Date)
                            .Select(i => new ExportMaintenanceInstanceDto(
                                i.Id, i.Date, i.Cost, i.Provider, i.Notes, i.CreatedAt))
                            .ToList()))
                    .ToList()))
            .ToList());

    // ========================================================================
    // Helpers
    // ========================================================================

    private static UserProfileDto ToProfile(User user) => new(
        user.Id,
        user.FirstName,
        user.LastName,
        user.Email,
        user.Theme,
        user.Language,
        user.CreatedAt,
        user.ConsentGivenAt,
        user.ConsentPolicyVersion,
        GdprPolicy.IsConsentRequired(user.ConsentGivenAt, user.ConsentPolicyVersion));

    /// <summary>
    /// Volet « métadonnées » de l'export : Art. 15(1)(a) à (h) et 15(2). Un export qui
    /// se limiterait aux données brutes ne satisferait pas le droit d'accès.
    /// </summary>
    private static ExportInformationDto BuildInformation() => new(
        Controller: "HouseFlow (BarbeRouss)",
        ContactEmail: GdprPolicy.PrivacyContactEmail,
        PolicyVersion: GdprPolicy.CurrentPolicyVersion,
        Purposes:
        [
            new("Create and operate your HouseFlow account (authentication, preferences).",
                "Création et fonctionnement de votre compte HouseFlow (authentification, préférences)."),
            new("Manage your houses, devices and maintenance history.",
                "Gestion de vos maisons, appareils et historique d'entretien."),
            new("Share a house with other users (memberships and invitations).",
                "Partage d'une maison avec d'autres utilisateurs (adhésions et invitations)."),
            new("Secure the service: audit trail, session management, API keys, abuse prevention.",
                "Sécurité du service : journal d'audit, gestion des sessions, clés API, prévention des abus.")
        ],
        DataCategories:
        [
            new("Identity: first name, last name, email address.",
                "Identité : prénom, nom, adresse email."),
            new("Preferences: theme, language.",
                "Préférences : thème, langue."),
            new("Property data: houses (name, address), devices, maintenance types and history (dates, costs, providers, notes).",
                "Données de patrimoine : maisons (nom, adresse), appareils, types et historique d'entretien (dates, coûts, prestataires, notes)."),
            new("Sharing data: memberships, roles, permissions, invitations sent and received.",
                "Données de partage : adhésions, rôles, permissions, invitations émises et reçues."),
            new("Technical data: sessions (dates, IP addresses), API keys (name, prefix, usage dates), audit logs (actions, IP, user agent).",
                "Données techniques : sessions (dates, adresses IP), clés API (nom, préfixe, dates d'usage), journaux d'audit (actions, IP, user agent)."),
            new("Your password is only stored as an irreversible hash and is never exported.",
                "Votre mot de passe n'est conservé que sous forme d'empreinte irréversible et n'est jamais exporté.")
        ],
        LegalBases:
        [
            new("Performance of the contract (Art. 6(1)(b)): account, houses, devices, maintenance, sharing.",
                "Exécution du contrat (Art. 6(1)(b)) : compte, maisons, appareils, entretiens, partage."),
            new("Legitimate interest (Art. 6(1)(f)): security, audit logs, sessions, invitations, abuse prevention.",
                "Intérêt légitime (Art. 6(1)(f)) : sécurité, journaux d'audit, sessions, invitations, prévention des abus.")
        ],
        Recipients:
        [
            new("Microsoft Azure (hosting and managed database), West Europe region (Netherlands), acting as a processor under Art. 28.",
                "Microsoft Azure (hébergement et base de données managée), région West Europe (Pays-Bas), sous-traitant au sens de l'Art. 28."),
            new("Other members of a house you share: they see the house, its devices and its maintenance history, and your first name and last name.",
                "Les autres membres d'une maison que vous partagez : ils voient la maison, ses appareils et son historique d'entretien, ainsi que vos prénom et nom."),
            new("No sale, no transfer to advertising partners, no third-party tracker.",
                "Aucune vente, aucune cession à des partenaires publicitaires, aucun traceur tiers.")
        ],
        Retention:
        [
            new("Account and house/device/maintenance data: for the lifetime of the account; immediate and permanent deletion on request.",
                "Compte et données de maisons/appareils/entretiens : durée de vie du compte ; suppression immédiate et définitive à la demande."),
            new("Inactive accounts: deleted after 3 years without login, following a notice.",
                "Comptes inactifs : supprimés après 3 ans sans connexion, après préavis."),
            new("Revoked or expired refresh tokens: purged 30 days after revocation/expiry.",
                "Refresh tokens révoqués ou expirés : purgés 30 jours après révocation/expiration."),
            new("Revoked API keys: purged 30 days after revocation.",
                "Clés API révoquées : purgées 30 jours après révocation."),
            new("IP addresses (audit logs, sessions, API keys): kept in full for 30 days, then truncated.",
                "Adresses IP (journaux d'audit, sessions, clés API) : complètes 30 jours, puis tronquées."),
            new("Audit logs: 1 year in identifying form, then anonymized; permanently purged after 3 years.",
                "Journaux d'audit : 1 an sous forme identifiante, puis anonymisés ; purge définitive à 3 ans."),
            new("Unaccepted, expired or revoked invitations: 30 days after expiry.",
                "Invitations non acceptées, expirées ou révoquées : 30 jours après expiration."),
            new("Backups: Azure PostgreSQL point-in-time restore, 7-day rotation.",
                "Sauvegardes : restauration ponctuelle Azure PostgreSQL, rotation de 7 jours.")
        ],
        Rights:
        [
            new("Access (Art. 15): this export.", "Accès (Art. 15) : le présent export."),
            new("Rectification (Art. 16): edit your profile in Settings.",
                "Rectification (Art. 16) : modifiez votre profil dans les paramètres."),
            new("Erasure (Art. 17): delete your account in Settings.",
                "Effacement (Art. 17) : supprimez votre compte dans les paramètres."),
            new("Restriction (Art. 18) and objection (Art. 21): write to the contact address below; objections to processing based on legitimate interest receive a reasoned written answer.",
                "Limitation (Art. 18) et opposition (Art. 21) : écrivez à l'adresse de contact ci-dessous ; toute opposition à un traitement fondé sur l'intérêt légitime reçoit une réponse écrite motivée."),
            new("Portability (Art. 20): this export in JSON or CSV, reusable by another service.",
                "Portabilité (Art. 20) : le présent export en JSON ou CSV, réutilisable par un autre service."),
            new("Lodge a complaint with a supervisory authority (Art. 77), in particular in your Member State of residence.",
                "Réclamation auprès d'une autorité de contrôle (Art. 77), notamment celle de votre État membre de résidence.")
        ],
        SupervisoryAuthorities:
        [
            new("Commission Nationale de l'Informatique et des Libertés (CNIL)", "France", "https://www.cnil.fr/fr/plaintes"),
            new("Autorité de protection des données (APD/GBA)", "Belgique", "https://www.autoriteprotectiondonnees.be/citoyen/agir/introduire-une-plainte")
        ],
        DataSource: new(
            "All data comes from you or from your use of the service. The only exception is an invitation you accepted, created by another user.",
            "Toutes les données proviennent de vous ou de votre usage du service. Seule exception : une invitation que vous avez acceptée, créée par un autre utilisateur."),
        InternationalTransfers: new(
            "Data is hosted in the European Union (Azure West Europe, Netherlands). No transfer outside the EEA is intended.",
            "Les données sont hébergées dans l'Union européenne (Azure West Europe, Pays-Bas). Aucun transfert hors EEE n'est prévu."),
        AutomatedDecisionMaking: new(
            "No automated decision-making or profiling within the meaning of Art. 22 is carried out. Maintenance statuses are simple date calculations with no legal effect.",
            "Aucune décision automatisée ni profilage au sens de l'Art. 22 n'est mis en œuvre. Les statuts d'entretien sont de simples calculs de dates, sans effet juridique."),
        // Art. 20 ne couvre que les données fournies par la personne et traitées sur la
        // base du contrat : les sessions, clés API et journaux d'audit en sont exclus.
        PortabilityScope: ["profile", "preferences", "houses", "memberships", "invitationsSent"]);
}
