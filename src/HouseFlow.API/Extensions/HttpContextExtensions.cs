namespace HouseFlow.API.Extensions;

/// <summary>
/// Helpers HTTP partagés par les contrôleurs.
/// </summary>
public static class HttpContextExtensions
{
    /// <summary>
    /// Adresse IP du client : en-tête <c>X-Forwarded-For</c> (premier maillon) si l'API
    /// est derrière un reverse proxy, sinon l'adresse de la connexion.
    /// </summary>
    public static string? GetClientIp(this HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwarded))
        {
            var first = forwarded.ToString().Split(',').FirstOrDefault()?.Trim();
            if (!string.IsNullOrEmpty(first)) return first;
        }

        return context.Connection.RemoteIpAddress?.ToString();
    }
}
