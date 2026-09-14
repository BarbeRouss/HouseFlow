namespace HouseFlow.Core.Entities;

/// <summary>
/// Refresh token for JWT token rotation
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; }

    /// <summary>
    /// The user this refresh token belongs to
    /// </summary>
    public required Guid UserId { get; set; }

    /// <summary>
    /// The actual refresh token value
    /// </summary>
    public required string Token { get; set; }

    /// <summary>
    /// Identifies the chain of tokens issued from one login (one per device/browser).
    /// Rotation keeps the FamilyId; reuse of a rotated token revokes the whole family.
    /// </summary>
    public Guid FamilyId { get; set; }

    /// <summary>
    /// Whether the session was opened with "remember me" (365-day sliding lifetime
    /// and persistent cookie) or not (24-hour lifetime and session cookie).
    /// </summary>
    public bool RememberMe { get; set; }

    /// <summary>
    /// When the refresh token expires
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// When the refresh token was created
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// IP address where the token was created
    /// </summary>
    public string? CreatedByIp { get; set; }

    /// <summary>
    /// When the refresh token was revoked (if revoked)
    /// </summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// IP address where the token was revoked
    /// </summary>
    public string? RevokedByIp { get; set; }

    /// <summary>
    /// The token that replaced this one (for token rotation)
    /// </summary>
    public string? ReplacedByToken { get; set; }

    /// <summary>
    /// Reason for revocation
    /// </summary>
    public string? ReasonRevoked { get; set; }

    /// <summary>
    /// Whether the refresh token is still active
    /// </summary>
    public bool IsActive => RevokedAt == null && !IsExpired;

    /// <summary>
    /// Whether the refresh token has expired
    /// </summary>
    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;

    // Navigation property
    public User? User { get; set; }
}
