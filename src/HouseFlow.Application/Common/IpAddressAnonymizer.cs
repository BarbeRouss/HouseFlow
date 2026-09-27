using System.Net;
using System.Net.Sockets;

namespace HouseFlow.Application.Common;

/// <summary>
/// RGPD Art. 5(1)(c) — minimisation des données. Les adresses IP sont des données
/// personnelles (CJUE, Breyer C-582/14). Cette classe tronque une adresse pour qu'elle
/// ne permette plus d'identifier une personne tout en conservant une information de
/// réseau approximative utile aux investigations de sécurité :
/// <list type="bullet">
///   <item>IPv4 : dernier octet mis à zéro (<c>192.168.1.42</c> → <c>192.168.1.0</c>)</item>
///   <item>IPv6 : les 80 derniers bits mis à zéro, seuls les 48 premiers bits (préfixe de
///   routage) sont conservés (<c>2001:db8:85a3:8d3:1319:8a2e:370:7348</c> → <c>2001:db8:85a3::</c>)</item>
/// </list>
/// Même méthode que l'anonymisation IP de référence des outils d'analytics acceptée par
/// les autorités de contrôle. Une valeur non parsable est remplacée par <c>null</c>
/// (on préfère perdre la donnée plutôt que conserver une valeur identifiante).
/// </summary>
public static class IpAddressAnonymizer
{
    /// <summary>Valeur utilisée à la place d'une IP après anonymisation complète (compte supprimé).</summary>
    public const string Redacted = "anonymized";

    public static string? Anonymize(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            return null;

        var trimmed = ipAddress.Trim();
        if (trimmed == Redacted)
            return Redacted;

        if (!IPAddress.TryParse(trimmed, out var ip))
            return null;

        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        var bytes = ip.GetAddressBytes();
        switch (ip.AddressFamily)
        {
            case AddressFamily.InterNetwork:
                bytes[3] = 0;
                return new IPAddress(bytes).ToString();

            case AddressFamily.InterNetworkV6:
                for (var i = 6; i < bytes.Length; i++) bytes[i] = 0;
                return new IPAddress(bytes).ToString();

            default:
                return null;
        }
    }

    /// <summary>
    /// True si l'adresse est déjà anonymisée (ou absente) — permet aux jobs de rétention
    /// d'être idempotents.
    /// </summary>
    public static bool IsAnonymized(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress)) return true;
        if (ipAddress == Redacted) return true;
        return Anonymize(ipAddress) == ipAddress;
    }
}
