using System.Text.Json.Serialization;

namespace HouseFlow.Web.Auth;

/// <summary>Authenticated user, held in memory by <see cref="TokenStore"/> (never persisted client-side).</summary>
public sealed class AuthUser
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("email")] public string Email { get; set; } = "";
    [JsonPropertyName("firstName")] public string FirstName { get; set; } = "";
    [JsonPropertyName("lastName")] public string LastName { get; set; } = "";
    [JsonPropertyName("theme")] public string? Theme { get; set; }
    [JsonPropertyName("language")] public string? Language { get; set; }
    [JsonPropertyName("isAdmin")] public bool IsAdmin { get; set; }

    /// <summary>
    /// True si l'utilisateur doit (ré)accepter les CGU et la politique de confidentialité en
    /// vigueur : la bannière de ré-acceptation reste affichée dans le tableau de bord tant que
    /// c'est le cas. Sérialisé avec le reste de l'utilisateur dans sessionStorage.
    /// </summary>
    [JsonPropertyName("consentRequired")] public bool ConsentRequired { get; set; }

    public string Initials =>
        $"{(FirstName.Length > 0 ? FirstName[0] : ' ')}{(LastName.Length > 0 ? LastName[0] : ' ')}".Trim();

    public string FullName => $"{FirstName} {LastName}".Trim();

    public static AuthUser FromDto(Api.UserDto u) => new()
    {
        Id = u.Id, Email = u.Email, FirstName = u.FirstName, LastName = u.LastName,
        Theme = u.Theme, Language = u.Language, IsAdmin = u.IsAdmin, ConsentRequired = u.ConsentRequired
    };
}
