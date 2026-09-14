namespace HouseFlow.Web;

/// <summary>
/// Constantes des documents légaux affichés par le frontend (politique de confidentialité,
/// Conditions générales d'utilisation).
/// </summary>
public static class LegalConstants
{
    /// <summary>
    /// Version (date ISO de dernière mise à jour) des CGU et de la politique de confidentialité,
    /// affichée sur /{locale}/privacy et /{locale}/terms et envoyée à POST /users/me/consent.
    /// </summary>
    /// <remarks>
    /// Cette valeur DOIT rester strictement égale à
    /// <c>HouseFlow.Application.Common.GdprPolicy.CurrentPolicyVersion</c> côté backend :
    /// le backend refuse (400) toute acceptation portant une autre version, et c'est elle qui
    /// détermine si un utilisateur doit ré-accepter (bannière). Toute modification substantielle
    /// des textes doit incrémenter les DEUX constantes ensemble.
    /// </remarks>
    public const string PolicyVersion = "2026-09-11";

    /// <summary>Adresse de contact pour l'exercice des droits (RGPD Art. 12-22).</summary>
    public const string PrivacyContactEmail = "privacy@houseflow.app";
}
