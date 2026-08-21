namespace CareerConnect.Api.Domain;

/// <summary>
/// One user's Gmail OAuth connection. Only the encrypted refresh token is
/// stored — access tokens are minted on demand and never persisted. Scope is
/// read-only (gmail.readonly); nothing in this app can send, delete, or
/// modify mail.
/// </summary>
public class GmailConnection
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    public required string ConnectedEmail { get; set; }
    public required string EncryptedRefreshToken { get; set; }

    public DateTime ConnectedAtUtc { get; set; }

    /// <summary>Watermark for the next scan's Gmail query — null before the first scan.</summary>
    public DateTime? LastCheckedAtUtc { get; set; }

    /// <summary>
    /// A GmailScanResponse, JSON-serialized, waiting for the user to see it —
    /// written by the background scan, read (and cleared) once by
    /// GET /api/gmail/pending-suggestions. Null when there's nothing pending.
    /// Opaque at this layer on purpose: Domain doesn't reference Contracts.
    /// </summary>
    public string? PendingScanResultJson { get; set; }

    public DateTime? PendingScanCompletedAtUtc { get; set; }

    public User User { get; set; } = null!;
}
