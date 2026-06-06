using System.Net.Http.Json;
using HouseFlow.Frontend.Wasm.Models;
using Microsoft.AspNetCore.Components.Authorization;

namespace HouseFlow.Frontend.Wasm.Services;

public sealed class AuthService(
    HttpClient http,
    TokenStore tokenStore,
    AuthenticationStateProvider authState)
{
    private HouseFlowAuthStateProvider Provider => (HouseFlowAuthStateProvider)authState;

    /// <summary>Logs in via /auth/login. Returns null on success, or an error message.</summary>
    public async Task<string?> LoginAsync(string email, string password)
    {
        try
        {
            var response = await http.PostAsJsonAsync("auth/login", new LoginRequest(email, password));
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return "Identifiants incorrects";
            if (!response.IsSuccessStatusCode)
                return $"Erreur ({(int)response.StatusCode})";

            var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
            if (auth?.AccessToken is null || auth.User is null)
                return "Réponse d'authentification invalide";

            await tokenStore.SetTokenAsync(auth.AccessToken);
            Provider.MarkAuthenticated(auth.User);
            return null;
        }
        catch (HttpRequestException)
        {
            return "Impossible de joindre le serveur";
        }
    }

    public async Task LogoutAsync()
    {
        try { await http.PostAsync("auth/logout", content: null); }
        catch (HttpRequestException) { /* best effort */ }
        await tokenStore.SetTokenAsync(null);
        Provider.MarkLoggedOut();
    }
}
