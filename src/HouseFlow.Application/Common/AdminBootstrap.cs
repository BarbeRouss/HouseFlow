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

    public static IReadOnlyList<string> GetBootstrapEmails(IConfiguration configuration) =>
        (configuration.GetSection(ConfigSection)?.GetChildren() ?? [])
            .Select(c => c.Value)
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e!.Trim())
            .ToArray();

    public static bool IsBootstrapAdmin(IConfiguration configuration, string email) =>
        GetBootstrapEmails(configuration)
            .Any(e => string.Equals(e, email, StringComparison.OrdinalIgnoreCase));
}
