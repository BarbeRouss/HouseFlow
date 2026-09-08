using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace HouseFlow.Web.Auth;

/// <summary>
/// Derives the authentication state from <see cref="TokenStore"/>: a user is
/// authenticated when both an access token and a stored user are present.
/// </summary>
public sealed class AppAuthStateProvider : AuthenticationStateProvider
{
    private readonly TokenStore _tokens;

    public AppAuthStateProvider(TokenStore tokens) => _tokens = tokens;

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var user = _tokens.User;
        var token = _tokens.AccessToken;

        ClaimsIdentity identity;
        if (user is not null && !string.IsNullOrEmpty(token))
        {
            identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Name, user.FullName),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim("firstName", user.FirstName),
                new Claim("lastName", user.LastName),
            }, authenticationType: "jwt");
        }
        else
        {
            identity = new ClaimsIdentity();
        }

        return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
    }

    public void NotifyChanged() => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
}
