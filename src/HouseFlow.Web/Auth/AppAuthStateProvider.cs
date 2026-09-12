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
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id),
                new(ClaimTypes.Name, user.FullName),
                new(ClaimTypes.Email, user.Email),
                new("firstName", user.FirstName),
                new("lastName", user.LastName),
            };
            if (user.IsAdmin) claims.Add(new Claim(ClaimTypes.Role, "Admin"));
            identity = new ClaimsIdentity(claims, authenticationType: "jwt");
        }
        else
        {
            identity = new ClaimsIdentity();
        }

        return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
    }

    public void NotifyChanged() => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
}
