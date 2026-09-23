namespace HouseFlow.API.Extensions;

/// <summary>
/// Helpers HTTP partagés par les contrôleurs.
/// </summary>
public static class HttpContextExtensions
{
    /// <summary>
    /// Adresse IP du client, telle que retenue par l'infrastructure.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Volontairement <b>pas</b> une lecture de <c>X-Forwarded-For</c>. Le pipeline applique
    /// déjà <c>UseForwardedHeaders</c>, qui ne consomme que le maillon ajouté par le reverse
    /// proxy de confiance et renseigne <c>Connection.RemoteIpAddress</c> ; les autres maillons
    /// de l'en-tête sont fournis par le client et falsifiables à volonté.
    /// </para>
    /// <para>
    /// Cette valeur alimente la <b>preuve d'acceptation des CGU</b> (RGPD Art. 5(2)), la trace
    /// d'export et l'audit trail. Lire l'en-tête brut permettrait à l'utilisateur de choisir
    /// l'IP inscrite dans sa propre preuve, et à un attaquant de brouiller l'investigation
    /// d'un incident : exactement ce que ces traces servent à établir.
    /// </para>
    /// </remarks>
    public static string? GetClientIp(this HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString();
}
