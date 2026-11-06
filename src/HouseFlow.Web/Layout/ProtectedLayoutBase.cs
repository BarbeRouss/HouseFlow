using HouseFlow.Web.Auth;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace HouseFlow.Web.Layout;

/// <summary>
/// Layouts of pages that need a session (app pages, onboarding setup). R6: without a session the
/// user is sent to <c>/login?returnUrl=&lt;current page&gt;</c> (history entry replaced), and
/// comes back there after logging in. Re-checked whenever the auth state changes (logout, failed
/// refresh).
/// </summary>
public abstract class ProtectedLayoutBase : LayoutComponentBase, IDisposable
{
    [Inject] protected TokenStore Tokens { get; set; } = default!;
    [Inject] protected NavigationManager Nav { get; set; } = default!;
    [Inject] protected AppAuthStateProvider AuthState { get; set; } = default!;
    [Inject] protected RedirectGuard Redirect { get; set; } = default!;

    protected bool IsAuthenticated => Tokens.User is not null && !string.IsNullOrEmpty(Tokens.AccessToken);

    protected override void OnInitialized()
    {
        AuthState.AuthenticationStateChanged += OnAuthChanged;
        Guard();
    }

    private void OnAuthChanged(Task<AuthenticationState> _) => InvokeAsync(() =>
    {
        Guard();
        StateHasChanged();
    });

    private void Guard()
    {
        if (IsAuthenticated) return;
        // An explicit logout navigates to the landing page itself (C1 "Se déconnecter" → P01).
        if (Redirect.ConsumeSigningOut()) return;
        Nav.NavigateTo(AppRoutes.LoginUrl(Nav), replace: true);
    }

    public virtual void Dispose() => AuthState.AuthenticationStateChanged -= OnAuthChanged;
}
