using System.Security.Cryptography;
using System.Text;

namespace HouseFlow.Application.Common;

/// <summary>
/// RGPD Art. 32(1)(a) — les refresh tokens sont stockés hashés en base, jamais en clair :
/// un vol de la base ne doit pas permettre de forger des sessions valides. Le porteur
/// (cookie <c>refreshToken</c>) conserve la valeur en clair ; la recherche en base se fait
/// sur le hash.
///
/// SHA-256 non salé est ici le bon choix (contrairement aux mots de passe) : le token est
/// une valeur aléatoire de 512 bits, non devinable par force brute ni par dictionnaire, et
/// le hash doit être déterministe pour permettre un lookup indexé.
/// </summary>
public static class TokenHasher
{
    /// <summary>Retourne le hash SHA-256 du token, en hexadécimal minuscule (64 caractères).</summary>
    public static string Hash(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexStringLower(bytes);
    }
}
