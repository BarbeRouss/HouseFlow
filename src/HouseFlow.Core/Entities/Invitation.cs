using HouseFlow.Core.Enums;

namespace HouseFlow.Core.Entities;

public class Invitation
{
    public Guid Id { get; set; }
    public required string Token { get; set; }

    /// <summary>
    /// Email of the invited person (RGPD: personal data of a non-user — see
    /// docs/gdpr/processing-register.md). Null for invitations created before it was required.
    /// Registration through this invitation must use this email.
    /// </summary>
    public string? Email { get; set; }
    public HouseRole Role { get; set; }
    public InvitationStatus Status { get; set; } = InvitationStatus.Pending;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime? DeclinedAt { get; set; }

    // Navigation properties
    public Guid HouseId { get; set; }
    public House? House { get; set; }
    public Guid CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    public Guid? AcceptedByUserId { get; set; }
    public User? AcceptedByUser { get; set; }
}
