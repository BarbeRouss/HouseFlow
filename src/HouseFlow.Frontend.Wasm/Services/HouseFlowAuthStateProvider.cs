using System.Security.Claims;
using HouseFlow.Frontend.Wasm.Models;
using Microsoft.AspNetCore.Components.Authorization;

namespace HouseFlow.Frontend.Wasm.Services;

/// <summary>
/// Drives <c>AuthorizeView</c>/<c>AuthorizeRouteView</c> from the access-token presence.
/// Token validity is enforced server-side; the client simply gates the UI.
/// </summary>
public sealed class HouseFlowAuthStateProvider(TokenStore tokenStore) : AuthenticationStateProvider
{
    private static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity());
    private ClaimsPrincipal _user = Anonymous;

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (_user.Identity?.IsAuthenticated != true)
        {
            var token = await tokenStore.GetTokenAsync();
            if (!string.IsNullOrEmpty(token))
                _user = BuildPrincipal(null);
        }
        return new AuthenticationState(_user);
    }

    public void MarkAuthenticated(UserDto user)
    {
        _user = BuildPrincipal(user);
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_user)));
    }

    public void MarkLoggedOut()
    {
        _user = Anonymous;
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_user)));
    }

    private static ClaimsPrincipal BuildPrincipal(UserDto? user)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, user?.Email ?? "user") };
        if (user is not null)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()));
            claims.Add(new Claim("firstName", user.FirstName));
        }
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "houseflow"));
    }
}
