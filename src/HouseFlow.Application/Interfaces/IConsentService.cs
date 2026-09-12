using HouseFlow.Application.DTOs;

namespace HouseFlow.Application.Interfaces;

/// <summary>
/// Enregistrement et consultation de l'acceptation des CGU / prise de connaissance de la
/// politique de confidentialité par un utilisateur existant (bannière de ré-acceptation).
/// </summary>
public interface IConsentService
{
    /// <summary>Statut d'acceptation courant de l'utilisateur.</summary>
    Task<ConsentStatusDto> GetConsentStatusAsync(Guid userId);

    /// <summary>
    /// Enregistre l'acceptation de la version en vigueur (date UTC + version). L'IP est
    /// journalisée par le contexte d'audit, preuve Art. 5(2) / 7(1).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Si <paramref name="accepted"/> est false ou si <paramref name="policyVersion"/> n'est
    /// pas la version en vigueur.
    /// </exception>
    Task<ConsentStatusDto> RecordConsentAsync(Guid userId, bool accepted, string policyVersion, string? ipAddress = null);
}
