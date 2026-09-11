namespace HouseFlow.Core.Entities;

public class User
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public string Theme { get; set; } = "system";
    public string Language { get; set; } = "fr";
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// RGPD Art. 7(1) — date (UTC) à laquelle l'utilisateur a accepté la politique de
    /// confidentialité et les CGU. Null pour les comptes créés avant l'introduction du
    /// consentement : ils doivent (ré)accepter la politique en vigueur.
    /// </summary>
    public DateTime? ConsentGivenAt { get; set; }

    /// <summary>
    /// Version de la politique de confidentialité acceptée (voir <c>GdprPolicy.CurrentVersion</c>).
    /// Une nouvelle version de la politique invalide le consentement précédent.
    /// </summary>
    public string? ConsentPolicyVersion { get; set; }

    // Navigation properties
    public ICollection<House> Houses { get; set; } = new List<House>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    public ICollection<HouseMember> HouseMemberships { get; set; } = new List<HouseMember>();
    public ICollection<ApiKey> ApiKeys { get; set; } = new List<ApiKey>();
}
