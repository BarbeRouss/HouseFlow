namespace HouseFlow.Web.Localization;

/// <summary>
/// Holds the locale for the current URL (the first path segment, e.g. /fr/...)
/// and exposes a convenience translation method so components don't have to pass
/// the locale on every call. Updated by <see cref="Components.LocaleBoundary"/>
/// on each navigation.
/// </summary>
public sealed class LocalizationState
{
    private readonly Localizer _localizer;

    public LocalizationState(Localizer localizer) => _localizer = localizer;

    public string Locale { get; private set; } = Localizer.DefaultLocale;

    public event Action? OnChange;

    public void SetLocale(string? locale)
    {
        var next = Localizer.IsSupported(locale) ? locale! : Localizer.DefaultLocale;
        if (next == Locale) return;
        Locale = next;
        OnChange?.Invoke();
    }

    public string T(string key, object? args = null) => _localizer.Translate(Locale, key, args);
}
