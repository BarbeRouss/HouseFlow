using Microsoft.Extensions.Configuration;

namespace HouseFlow.Application.Common;

/// <summary>
/// Bootstrap administrators: e-mail addresses listed under <c>Admin:BootstrapEmails</c>
/// (appsettings.json, or <c>Admin__BootstrapEmails__N</c> environment variables) are
/// granted the admin flag automatically — at API startup when the account already exists,
/// and at registration otherwise. This is how the very first administrator gets in;
/// further admins are promoted from the admin interface.
/// </summary>
public static class AdminBootstrap
{
    public const string AdminRole = "Admin";
    public const string ConfigSection = "Admin:BootstrapEmails";

    /// <summary>The demo account seeded by the API when DEMO_MODE is enabled (PR previews, local dev).</summary>
    public const string DemoEmail = "demo@demo.com";

    public static bool IsDemoMode(IConfiguration configuration) =>
        string.Equals(configuration["DEMO_MODE"], "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Configured bootstrap e-mails, plus the demo account when DEMO_MODE is on: demo
    /// environments showcase the admin interface too, and DEMO_MODE is never enabled in
    /// production.
    /// </summary>
    public static IReadOnlyList<string> GetBootstrapEmails(IConfiguration configuration)
    {
        var emails = (configuration.GetSection(ConfigSection)?.GetChildren() ?? [])
            .Select(c => c.Value)
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e!.Trim())
            .ToList();

        if (IsDemoMode(configuration) && !emails.Contains(DemoEmail, StringComparer.OrdinalIgnoreCase))
        {
            emails.Add(DemoEmail);
        }

        return emails;
    }

    public static bool IsBootstrapAdmin(IConfiguration configuration, string email) =>
        GetBootstrapEmails(configuration)
            .Any(e => string.Equals(e, email, StringComparison.OrdinalIgnoreCase));
}
