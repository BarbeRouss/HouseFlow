using HouseFlow.Web.Api;
using HouseFlow.Web.Auth;
using Microsoft.AspNetCore.Components;

namespace HouseFlow.Web.Services;

/// <summary>
/// Ends the browser session in one place, for every exit path: "Se déconnecter" (C1 header menu
/// and P11 menu → P01), account deletion (M7 → P02, history replaced) and a refresh refused
/// because the account is restricted (→ P02 with the art. 18 message).
/// </summary>
public sealed class SessionService
{
    private readonly ApiService _api;
    private readonly TokenStore _tokens;
    private readonly NavCounterService _counters;
    private readonly ToastService _toasts;
    private readonly RedirectGuard _redirect;
    private readonly AppAuthStateProvider _authState;
    private readonly NavigationManager _nav;

    public SessionService(ApiService api, TokenStore tokens, NavCounterService counters, ToastService toasts,
        RedirectGuard redirect, AppAuthStateProvider authState, NavigationManager nav)
    {
        _api = api;
        _tokens = tokens;
        _counters = counters;
        _toasts = toasts;
        _redirect = redirect;
        _authState = authState;
        _nav = nav;
    }

    /// <summary>"Se déconnecter": revokes the refresh token server side, then → P01 (landing).</summary>
    public async Task SignOutAsync()
    {
        await _api.LogoutAsync();
        await EndAsync($"/{AppRoutes.CurrentLocale(_nav)}");
    }

    /// <summary>
    /// Clears the local session (the server side is already gone, e.g. after M7) and navigates to
    /// <paramref name="target"/>, replacing the history entry.
    /// </summary>
    public async Task EndAsync(string target)
    {
        await _tokens.ClearAsync();
        _counters.Reset();
        _toasts.Dismiss();
        // The protected layouts react to the auth change by sending the user to
        // /login?returnUrl=…: this flag tells them the caller navigates itself.
        _redirect.SetSigningOut();
        _authState.NotifyChanged();
        _nav.NavigateTo(target, replace: true);
        // The layout guards ran synchronously on NotifyChanged; drop the flag if none consumed it
        // (e.g. from an error page) so a later expired session still redirects to login.
        _redirect.ConsumeSigningOut();
    }
}
