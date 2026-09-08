using System.Text.Json.Serialization;

namespace HouseFlow.Web.Auth;

/// <summary>Authenticated user, persisted in sessionStorage as houseflow_auth_user.</summary>
public sealed class AuthUser
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("email")] public string Email { get; set; } = "";
    [JsonPropertyName("firstName")] public string FirstName { get; set; } = "";
    [JsonPropertyName("lastName")] public string LastName { get; set; } = "";
    [JsonPropertyName("theme")] public string? Theme { get; set; }
    [JsonPropertyName("language")] public string? Language { get; set; }

    public string Initials =>
        $"{(FirstName.Length > 0 ? FirstName[0] : ' ')}{(LastName.Length > 0 ? LastName[0] : ' ')}".Trim();

    public string FullName => $"{FirstName} {LastName}".Trim();
}
