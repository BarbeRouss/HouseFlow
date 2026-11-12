using System.Net;
using System.Net.Sockets;
using IPNetwork = System.Net.IPNetwork;

namespace HouseFlow.API.Configuration;

/// <summary>
/// Configuration section <c>HttpEdge</c>: which reverse proxies the API trusts. Absent, the Azure
/// Container Apps topology applies (<see cref="HttpEdge.DefaultTrustedProxyNetworks"/>, one hop) —
/// Azure environments need no configuration. The homelab previews (issue #351) sit behind two
/// Traefik proxies in a row and set both values:
/// <code>
/// HttpEdge__TrustedProxyNetworks__0=172.20.0.0/16,192.168.150.1
/// HttpEdge__ForwardLimit=2
/// </code>
/// </summary>
public sealed class HttpEdgeOptions
{
    public const string SectionName = "HttpEdge";

    /// <summary>
    /// CIDR ranges or single addresses whose <c>X-Forwarded-*</c> headers are believed. Each element
    /// may list several, comma-separated (like <c>CORS__ORIGINS</c>), so one environment variable
    /// is enough. <b>When set, it replaces the defaults</b> instead of adding to them: a topology
    /// that does not sit behind the Azure ingress has no reason to trust its range.
    /// </summary>
    public string[] TrustedProxyNetworks { get; set; } = [];

    /// <summary>
    /// Entries consumed from the right of <c>X-Forwarded-For</c>: the number of trusted proxies in a
    /// row. Anything the client put further left is never believed. At least 1.
    /// </summary>
    public int ForwardLimit { get; set; } = HttpEdge.DefaultForwardLimit;

    /// <summary>Reads the section; fails fast (at the first request pipeline build) on an invalid value.</summary>
    public static HttpEdgeOptions From(IConfiguration configuration)
    {
        var options = configuration.GetSection(SectionName).Get<HttpEdgeOptions>() ?? new HttpEdgeOptions();
        if (options.ForwardLimit < 1)
            throw new InvalidOperationException("HttpEdge:ForwardLimit must be at least 1.");
        // Validates the networks now rather than on the first forwarded request.
        _ = options.ResolveTrustedProxyNetworks();
        return options;
    }

    /// <summary>The configured networks, or the Azure defaults when none is configured.</summary>
    public IReadOnlyList<IPNetwork> ResolveTrustedProxyNetworks()
    {
        var entries = TrustedProxyNetworks
            .SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToList();
        return entries.Count == 0 ? HttpEdge.DefaultTrustedProxyNetworks : entries.Select(ParseNetwork).ToList();
    }

    /// <summary>A CIDR range (<c>172.20.0.0/16</c>) or a single address (<c>192.168.150.1</c>, a /32 or /128).</summary>
    public static IPNetwork ParseNetwork(string value)
    {
        if (value.Contains('/'))
        {
            return IPNetwork.TryParse(value, out var network)
                ? network
                : throw new InvalidOperationException($"HttpEdge:TrustedProxyNetworks: '{value}' is not a valid CIDR range.");
        }

        if (!IPAddress.TryParse(value, out var address))
            throw new InvalidOperationException($"HttpEdge:TrustedProxyNetworks: '{value}' is not a valid IP address or CIDR range.");

        return new IPNetwork(address, address.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32);
    }
}
