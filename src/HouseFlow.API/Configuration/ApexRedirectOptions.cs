namespace HouseFlow.API.Configuration;

/// <summary>
/// Configuration section <c>ApexRedirect</c> (issue #217): the bare domain (<c>houseflow.cloud</c>)
/// has no HTTPS of its own — the OVH web redirection serves plain HTTP only, and the frontend (a
/// Static Web App) cannot be bound to an apex with a CNAME. Production therefore binds the apex to
/// the API's Container App, which already holds the wildcard certificate, and this section turns
/// every request for that host into a permanent redirect to the canonical <c>www</c> host.
/// <code>
/// ApexRedirect__Host=houseflow.cloud
/// ApexRedirect__Target=https://www.houseflow.cloud
/// </code>
/// Absent (development, tests, PR previews), the middleware is not registered at all.
/// </summary>
public sealed class ApexRedirectOptions
{
    public const string SectionName = "ApexRedirect";

    /// <summary>The host name to redirect, without scheme or port (<c>houseflow.cloud</c>).</summary>
    public string? Host { get; set; }

    /// <summary>The canonical origin to redirect to (<c>https://www.houseflow.cloud</c>): an absolute https URL, no path.</summary>
    public string? Target { get; set; }

    public bool Enabled => !string.IsNullOrWhiteSpace(Host);

    /// <summary>Reads the section; fails fast at startup when it is half-configured or invalid.</summary>
    public static ApexRedirectOptions From(IConfiguration configuration)
    {
        var options = configuration.GetSection(SectionName).Get<ApexRedirectOptions>() ?? new ApexRedirectOptions();
        if (!options.Enabled) return options;

        options.Host = options.Host!.Trim();
        if (options.Host.Contains("://") || options.Host.Contains('/') || options.Host.Contains(':'))
            throw new InvalidOperationException("ApexRedirect:Host must be a bare host name (no scheme, port or path).");

        if (!Uri.TryCreate(options.Target, UriKind.Absolute, out var target)
            || target.Scheme != Uri.UriSchemeHttps
            || target.AbsolutePath != "/"
            || !string.IsNullOrEmpty(target.Query)
            || !string.IsNullOrEmpty(target.Fragment))
            throw new InvalidOperationException("ApexRedirect:Target must be an absolute https origin without path (https://www.example.com) when ApexRedirect:Host is set.");

        if (string.Equals(target.Host, options.Host, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("ApexRedirect:Target must not point back to ApexRedirect:Host (redirect loop).");

        options.Target = target.GetLeftPart(UriPartial.Authority);
        return options;
    }
}
