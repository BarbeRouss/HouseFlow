using HouseFlow.Web.Localization;
using Microsoft.AspNetCore.Components;

namespace HouseFlow.Web.Auth;

/// <summary>Nav entry highlighted by the header (C1) and the tab bar (C2).</summary>
public enum NavSection { None, Home, Houses, Account }

/// <summary>
/// Route helpers shared by the layouts, the auth pipeline and the pages: current locale,
/// R6 redirects with <c>?returnUrl=</c>, and the active nav section.
/// </summary>
public static class AppRoutes
{
    /// <summary>
    /// A house / device id as the API issues them (GUID). A malformed id (<c>/houses/not-a-guid</c>)
    /// can match nothing: the detail pages answer P13 404 without asking the API, which would
    /// reject it with a 400 (shown otherwise as the generic « Impossible de charger » error).
    /// </summary>
    public static bool IsResourceId(string? id) => Guid.TryParse(id, out _);

    /// <summary>What a detail GET throws for an id that cannot exist (see <see cref="IsResourceId"/>): P13 404.</summary>
    public static Api.ApiException NotFound() => new(404, "Not found", Api.ApiErrorCodes.NotFound);

    /// <summary>Locale from the first path segment ("fr" when missing or unsupported).</summary>
    public static string LocaleOf(string uri)
    {
        var first = PathOf(uri).Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return Localizer.IsSupported(first) ? first! : Localizer.DefaultLocale;
    }

    public static string CurrentLocale(NavigationManager nav) => LocaleOf(nav.Uri);

    /// <summary>
    /// R6: the login page, carrying the current location as returnUrl (so the user comes back to
    /// the page they asked for). No returnUrl when already on a guest page.
    /// </summary>
    public static string LoginUrl(NavigationManager nav)
    {
        var locale = CurrentLocale(nav);
        var relative = "/" + nav.ToBaseRelativePath(nav.Uri);
        return SafeReturnUrl(relative) is { } ret
            ? $"/{locale}/login?returnUrl={Uri.EscapeDataString(ret)}"
            : $"/{locale}/login";
    }

    /// <summary>
    /// Validates a returnUrl: an app-relative path only (no scheme, no host, no "//" or "/\"),
    /// and never a guest page (login/register) or the landing page. Null when unusable.
    /// </summary>
    public static string? SafeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl)) return null;
        var url = returnUrl.Trim();
        if (!url.StartsWith('/') || url.StartsWith("//") || url.StartsWith("/\\")) return null;
        if (url.Contains("://") || url.Any(char.IsControl)) return null;

        var segments = PathOf(url).Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return null;                                   // "/"
        var rest = Localizer.IsSupported(segments[0]) ? segments.Skip(1).ToArray() : segments;
        if (rest.Length == 0) return null;                                       // "/fr" (landing)
        if (rest[0] is "login" or "register") return null;
        return url;
    }

    /// <summary>Value of the login page's <c>?reason=</c> when a refresh was refused for a restricted account.</summary>
    public const string RestrictedReason = "restricted";

    /// <summary>
    /// P02 showing the art. 18 "compte suspendu" message: where the session ends when the API
    /// answers <c>account_restricted</c> to a refresh (boot or mid-session).
    /// </summary>
    public static string RestrictedLoginUrl(string locale) => $"/{locale}/login?reason={RestrictedReason}";

    /// <summary>Where to go after a successful login: the safe returnUrl, else P07.</summary>
    public static string AfterLogin(string? returnUrl, string locale) =>
        SafeReturnUrl(returnUrl) ?? $"/{locale}/dashboard";

    /// <summary>
    /// Invitation token of a P04 URL (<c>/{locale}/invitations/{token}[?…]</c>), e.g. the returnUrl
    /// P02 forwards to P03 when the visitor came from an invitation. Null for any other URL.
    /// </summary>
    public static string? InvitationTokenOf(string? url)
    {
        if (SafeReturnUrl(url) is not { } safe) return null;
        var segments = PathOf(safe).Split('/', StringSplitOptions.RemoveEmptyEntries);
        var rest = Localizer.IsSupported(segments[0]) ? segments.Skip(1).ToArray() : segments;
        return rest.Length == 2 && rest[0] == "invitations" && rest[1].Length > 0
            ? Uri.UnescapeDataString(rest[1])
            : null;
    }

    /// <summary>First path segments of the app's pages (every route is /{locale}/…).</summary>
    private static readonly HashSet<string> KnownRoots = new(StringComparer.OrdinalIgnoreCase)
    {
        "dashboard", "login", "register", "houses", "devices", "settings", "admin",
        "privacy", "terms", "invitations", "setup",
    };

    /// <summary>
    /// Old bookmarks and links without a locale (<c>/dashboard</c>, <c>/houses/{id}</c>,
    /// <c>/invitations/{token}?…</c>): the same page under <paramref name="locale"/>, query and
    /// fragment kept. Null when the path already has a locale or is not an app page (→ 404).
    /// </summary>
    public static string? LocalelessRedirect(string relativeUri, string locale)
    {
        var url = "/" + relativeUri.TrimStart('/');
        var cut = url.IndexOfAny(new[] { '?', '#' });
        var path = cut >= 0 ? url[..cut] : url;
        var first = path.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (first is null || Localizer.IsSupported(first) || !KnownRoots.Contains(first)) return null;
        return $"/{locale}{url}";
    }

    /// <summary>Nav section of a URL: /dashboard → Home; /houses, /devices → Houses; /settings, /admin → Account.</summary>
    public static NavSection SectionOf(string uri)
    {
        var segments = PathOf(uri).Split('/', StringSplitOptions.RemoveEmptyEntries);
        var rest = segments.Length > 0 && Localizer.IsSupported(segments[0]) ? segments.Skip(1) : segments;
        return rest.FirstOrDefault() switch
        {
            "dashboard" => NavSection.Home,
            "houses" or "devices" => NavSection.Houses,
            "settings" or "admin" => NavSection.Account,
            _ => NavSection.None,
        };
    }

    private static string PathOf(string uriOrPath)
    {
        if (Uri.TryCreate(uriOrPath, UriKind.Absolute, out var abs) && abs.Scheme is "http" or "https")
            return abs.AbsolutePath;
        var cut = uriOrPath.IndexOfAny(new[] { '?', '#' });
        return cut >= 0 ? uriOrPath[..cut] : uriOrPath;
    }
}
