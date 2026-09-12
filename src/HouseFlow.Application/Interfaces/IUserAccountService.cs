using HouseFlow.Application.DTOs;

namespace HouseFlow.Application.Interfaces;

/// <summary>
/// Exercice en self-service des droits RGPD de la personne concernée :
/// accès (Art. 15), rectification (Art. 16), effacement (Art. 17) et
/// portabilité (Art. 20).
/// </summary>
public interface IUserAccountService
{
    /// <summary>Art. 15 — profil de l'utilisateur connecté.</summary>
    Task<UserProfileDto> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Art. 16 — rectification du prénom, nom et email. L'email doit rester unique
    /// (sinon <see cref="InvalidOperationException"/>).
    /// </summary>
    Task<UserProfileDto> UpdateProfileAsync(Guid userId, UpdateProfileRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Art. 17 — suppression immédiate et définitive du compte, après ressaisie du
    /// mot de passe. Les journaux d'audit sont anonymisés, pas supprimés.
    /// </summary>
    Task DeleteAccountAsync(Guid userId, string password, string? ipAddress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Art. 15 + 20 — copie complète des données personnelles. Limité à un export
    /// par heure et par utilisateur (<see cref="Common.TooManyRequestsException"/>).
    /// </summary>
    Task<UserDataExportDto> ExportDataAsync(Guid userId, string? ipAddress = null, CancellationToken cancellationToken = default);
}
