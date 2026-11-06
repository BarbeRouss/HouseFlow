namespace HouseFlow.Application.Common;

/// <summary>
/// Forme canonique d'une adresse e-mail : sans espaces autour, en minuscules. Une adresse est
/// l'identifiant de connexion ; les claviers mobiles mettent volontiers une majuscule initiale et
/// un invitant peut taper n'importe quelle casse. Toute adresse est donc normalisée <b>avant</b>
/// d'être stockée (inscription, rectification du profil, invitation) comme avant d'être
/// recherchée (connexion, unicité) : les comparaisons peuvent alors rester exactes et profiter de
/// l'index unique de <c>Users.Email</c>. Les lignes antérieures ont été alignées par la migration
/// <c>NormalizeEmailCase</c>.
/// </summary>
public static class EmailNormalizer
{
    public static string Normalize(string email) => email.Trim().ToLowerInvariant();
}
