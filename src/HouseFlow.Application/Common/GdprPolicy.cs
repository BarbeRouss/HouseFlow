namespace HouseFlow.Application.Common;

/// <summary>
/// Constantes RGPD partagées entre backend, documentation et frontend.
/// </summary>
public static class GdprPolicy
{
    /// <summary>
    /// Version en vigueur de la politique de confidentialité et des CGU (date ISO de
    /// dernière mise à jour). Toute modification substantielle de la politique doit
    /// incrémenter cette valeur : les utilisateurs ayant accepté une version antérieure
    /// (ou aucune) devront ré-accepter (bannière de re-consentement).
    /// Doit rester alignée avec la date affichée sur les pages /privacy et /terms du frontend.
    /// </summary>
    public const string CurrentPolicyVersion = "2026-09-23";

    /// <summary>Nom d'utilisateur substitué dans les journaux d'audit d'un compte supprimé.</summary>
    public const string DeletedUserName = "deleted-user";

    /// <summary>Adresse de contact pour l'exercice des droits (Art. 12-22) et le DPO/point de contact.</summary>
    public const string PrivacyContactEmail = "privacy@houseflow.cloud";

    /// <summary>True si l'utilisateur doit (ré)accepter la politique en vigueur.</summary>
    public static bool IsConsentRequired(DateTime? consentGivenAt, string? consentPolicyVersion) =>
        consentGivenAt is null || consentPolicyVersion != CurrentPolicyVersion;
}
