namespace HouseFlow.API.Authentication;

/// <summary>
/// Forme unique du cookie de refresh. Un navigateur n'honore une suppression que si les
/// attributs (nom, chemin, SameSite, Secure) correspondent au cookie qu'il a stocké, et
/// plusieurs contrôleurs le posent ou l'effacent (authentification, suppression de compte) :
/// la connaissance de ces attributs vit donc ici, à un seul endroit.
/// </summary>
public static class RefreshTokenCookie
{
    /// <summary>Nom du cookie HttpOnly portant le jeton de rafraîchissement.</summary>
    public const string Name = "refreshToken";

    /// <summary>
    /// Portée du cookie : il n'est envoyé qu'aux endpoints d'authentification qui en ont
    /// besoin, et non à toute l'API (minimisation, RGPD Art. 25 et 32).
    /// </summary>
    public const string CookiePath = "/api/v1/auth";

    /// <summary>
    /// « Lax » (défaut) protège /auth/refresh et /auth/logout contre le CSRF. « None » n'est
    /// destiné qu'aux déploiements où le frontend et l'API vivent sur des sites différents
    /// (previews de PR : Static Web App + Container App), où un cookie Lax ne serait ni
    /// stocké ni renvoyé.
    /// </summary>
    public static SameSiteMode ResolveSameSite(IConfiguration configuration) =>
        Enum.TryParse<SameSiteMode>(configuration["Auth:CookieSameSite"], ignoreCase: true, out var mode)
            ? mode
            : SameSiteMode.Lax;

    /// <param name="expires">
    /// Expiration d'un cookie persistant (« se souvenir de moi ») ; null pour un cookie de
    /// session, que le navigateur oublie à sa fermeture.
    /// </param>
    public static CookieOptions Options(HttpRequest request, SameSiteMode sameSite, DateTime? expires) => new()
    {
        HttpOnly = true, // inaccessible au JavaScript (protection XSS)
        // Les navigateurs exigent Secure avec SameSite=None ; les hôtes loopback l'acceptent en HTTP simple.
        Secure = request.IsHttps || sameSite == SameSiteMode.None,
        SameSite = sameSite,
        Expires = expires,
        Path = CookiePath,
        IsEssential = true // cookie strictement nécessaire — exempté de consentement (art. 82 loi Informatique et Libertés)
    };

    public static void Append(HttpResponse response, string refreshToken, SameSiteMode sameSite, DateTime? expires) =>
        response.Cookies.Append(Name, refreshToken, Options(response.HttpContext.Request, sameSite, expires));

    /// <summary>Efface le cookie avec les mêmes attributs que lors de sa pose, sans quoi le navigateur le conserve.</summary>
    public static void Clear(HttpResponse response, SameSiteMode sameSite) =>
        response.Cookies.Delete(Name, Options(response.HttpContext.Request, sameSite, expires: null));
}
