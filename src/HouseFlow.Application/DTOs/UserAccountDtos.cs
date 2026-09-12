namespace HouseFlow.Application.DTOs;

// UpdateProfileRequestDto → generated as HouseFlow.Contracts.UpdateProfileRequest (see ContractAliases.cs)
// DeleteAccountRequestDto → generated as HouseFlow.Contracts.DeleteAccountRequest (see ContractAliases.cs)

/// <summary>
/// Profil de l'utilisateur connecté (schéma OpenAPI <c>UserProfile</c>).
/// RGPD Art. 15 (accès aux données de profil) et Art. 16 (rectification).
/// </summary>
public record UserProfileDto(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string Theme,
    string Language,
    DateTime CreatedAt,
    DateTime? ConsentGivenAt,
    string? ConsentPolicyVersion,
    bool ConsentRequired
);

// ============================================================================
// Export RGPD (Art. 15 — accès, Art. 20 — portabilité), schéma OpenAPI UserDataExport.
// Aucun secret technique (PasswordHash, Token, ReplacedByToken, KeyHash) n'y figure,
// ni aucune donnée identifiant un tiers (Art. 15(4) / 20(4)).
// ============================================================================

public record UserDataExportDto(
    DateTime ExportedAt,
    string FormatVersion,
    ExportProfileDto Profile,
    ExportPreferencesDto Preferences,
    ExportConsentDto Consent,
    IReadOnlyList<ExportHouseDto> Houses,
    IReadOnlyList<ExportMembershipDto> Memberships,
    IReadOnlyList<ExportInvitationSentDto> InvitationsSent,
    IReadOnlyList<ExportInvitationReceivedDto> InvitationsReceived,
    IReadOnlyList<ExportApiKeyDto> ApiKeys,
    IReadOnlyList<ExportSessionDto> Sessions,
    IReadOnlyList<ExportAuditLogDto> AuditLogs,
    ExportInformationDto Information
);

public record ExportProfileDto(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    DateTime? LastLoginAt
);

public record ExportPreferencesDto(
    string Theme,
    string Language
);

public record ExportConsentDto(
    DateTime? ConsentGivenAt,
    string? ConsentPolicyVersion
);

public record ExportHouseDto(
    Guid Id,
    string Name,
    string? Address,
    string? ZipCode,
    string? City,
    string? Country,
    DateTime CreatedAt,
    IReadOnlyList<ExportDeviceDto> Devices
);

public record ExportDeviceDto(
    Guid Id,
    string Name,
    string Type,
    string? Brand,
    string? Model,
    DateTime? InstallDate,
    DateTime CreatedAt,
    IReadOnlyList<ExportMaintenanceTypeDto> MaintenanceTypes
);

/// <summary>
/// <paramref name="Status"/> (up_to_date / pending / overdue) est une donnée *dérivée*
/// calculée par HouseFlow : elle relève de l'Art. 15 (accès) mais pas de l'Art. 20
/// (portabilité — WP242 exclut les données inférées).
/// </summary>
public record ExportMaintenanceTypeDto(
    Guid Id,
    string Name,
    string Periodicity,
    int? CustomDays,
    string Status,
    IReadOnlyList<ExportMaintenanceInstanceDto> Instances
);

public record ExportMaintenanceInstanceDto(
    Guid Id,
    DateTime Date,
    decimal? Cost,
    string? Provider,
    string? Notes,
    DateTime CreatedAt
);

/// <summary>
/// Maison partagée par un tiers. Art. 15(4) / 20(4) : ni le nom ni l'email du
/// propriétaire ou des autres membres ne sont exportés.
/// </summary>
public record ExportMembershipDto(
    Guid HouseId,
    string HouseName,
    string Role,
    bool CanLogMaintenance,
    bool CanViewCosts,
    DateTime Since
);

/// <summary>Invitation émise par l'utilisateur — sans le token (secret) ni l'identité de l'acceptant.</summary>
public record ExportInvitationSentDto(
    Guid Id,
    string HouseName,
    string Role,
    string Status,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    DateTime? AcceptedAt
);

/// <summary>Invitation acceptée par l'utilisateur — sans le token ni l'identité de l'émetteur.</summary>
public record ExportInvitationReceivedDto(
    Guid Id,
    string HouseName,
    string Role,
    DateTime? AcceptedAt
);

/// <summary>Clé API — jamais le hash (<c>KeyHash</c>).</summary>
public record ExportApiKeyDto(
    string Name,
    string Prefix,
    string Scope,
    DateTime CreatedAt,
    DateTime? LastUsedAt,
    DateTime? RevokedAt
);

/// <summary>Session (refresh token) — jamais la valeur du token ni son remplaçant.</summary>
public record ExportSessionDto(
    DateTime CreatedAt,
    DateTime ExpiresAt,
    string? CreatedByIp,
    DateTime? RevokedAt,
    string? RevokedByIp,
    string? ReasonRevoked
);

/// <summary>Journal d'audit — sans les valeurs avant/après, qui peuvent contenir des données de tiers.</summary>
public record ExportAuditLogDto(
    DateTime Timestamp,
    string Action,
    string EntityType,
    string EntityId,
    string? ChangedProperties,
    string? IpAddress,
    string? UserAgent
);

/// <summary>Texte bilingue (l'export est fourni en anglais et en français).</summary>
public record LocalizedTextDto(string En, string Fr);

public record SupervisoryAuthorityDto(string Name, string Country, string Url);

/// <summary>
/// Volet « métadonnées » de l'export : les informations de l'Art. 15(1)(a) à (h)
/// et 15(2), sans lesquelles un export n'est qu'un dump de base de données.
/// </summary>
public record ExportInformationDto(
    string Controller,
    string ContactEmail,
    string PolicyVersion,
    IReadOnlyList<LocalizedTextDto> Purposes,
    IReadOnlyList<LocalizedTextDto> DataCategories,
    IReadOnlyList<LocalizedTextDto> LegalBases,
    IReadOnlyList<LocalizedTextDto> Recipients,
    IReadOnlyList<LocalizedTextDto> Retention,
    IReadOnlyList<LocalizedTextDto> Rights,
    IReadOnlyList<SupervisoryAuthorityDto> SupervisoryAuthorities,
    LocalizedTextDto DataSource,
    LocalizedTextDto InternationalTransfers,
    LocalizedTextDto AutomatedDecisionMaking,
    IReadOnlyList<string> PortabilityScope
);
