using Microsoft.JSInterop;

namespace HouseFlow.Web;

/// <summary>
/// Manages the light/dark/system theme, applying it as the `dark`/`light` class
/// on &lt;html&gt; (via hf.applyTheme in app.js) and persisting the choice in
/// localStorage. Mirrors the former next-themes setup.
/// </summary>
public sealed class ThemeService
{
    private readonly IJSRuntime _js;

    public ThemeService(IJSRuntime js) => _js = js;

    public string Current { get; private set; } = "system";

    public event Action? OnChange;

    public async Task InitializeAsync()
    {
        Current = await _js.InvokeAsync<string>("hf.getTheme");
    }

    public async Task SetThemeAsync(string theme)
    {
        Current = theme;
        await _js.InvokeVoidAsync("hf.applyTheme", theme);
        OnChange?.Invoke();
    }
}
