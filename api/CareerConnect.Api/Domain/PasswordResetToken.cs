namespace CareerConnect.Api.Domain;

/// <summary>
/// One outstanding "reset my password" link.
///
/// Only a hash of the token is stored. The raw value exists in the email and
/// nowhere else, so a leaked database backup can't be turned into working
/// reset links for every account in it.
/// </summary>
public class PasswordResetToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>SHA-256 of the raw token, hex encoded. The raw token is never persisted.</summary>
    public required string TokenHash { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>Set the moment it's redeemed — a reset link works exactly once.</summary>
    public DateTime? UsedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public User User { get; set; } = null!;
}
