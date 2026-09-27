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
    public const string CurrentPolicyVersion = "2026-09-26";

    /// <summary>Nom d'utilisateur substitué dans les journaux d'audit d'un compte supprimé.</summary>
    public const string DeletedUserName = "deleted-user";

    /// <summary>Adresse de contact pour l'exercice des droits (Art. 12-22) et le DPO/point de contact.</summary>
    public const string PrivacyContactEmail = "privacy@houseflow.cloud";

    /// <summary>
    /// Raison sociale du responsable du traitement, telle qu'elle doit apparaître partout où
    /// l'Art. 13(1)(a) et l'Art. 15(1) l'exigent : pages légales et volet « métadonnées » de
    /// l'export de données. Un pseudonyme ou un nom de produit n'identifie pas le responsable.
    /// </summary>
    public const string ControllerName = "Rouss Consulting SRL";

    /// <summary>True si l'utilisateur doit (ré)accepter la politique en vigueur.</summary>
    public static bool IsConsentRequired(DateTime? consentGivenAt, string? consentPolicyVersion) =>
        consentGivenAt is null || consentPolicyVersion != CurrentPolicyVersion;
}
