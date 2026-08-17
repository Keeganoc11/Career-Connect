using Google.Apis.Gmail.v1;

namespace CareerConnect.Api.Services;

public record GmailConnectionInfo(string ConnectedEmail, DateTime ConnectedAtUtc, DateTime? LastCheckedAtUtc);

/// <summary>
/// Owns the Gmail OAuth lifecycle: building the consent URL, exchanging the
/// authorization code, and minting an authenticated <see cref="GmailService"/>
/// from the stored (encrypted) refresh token. Only the refresh token is ever
/// persisted — access tokens are minted per call and never stored.
/// </summary>
public interface IGmailOAuthService
{
    /// <summary>False when no Google OAuth client is configured — the app still runs, this feature is just disabled.</summary>
    bool IsConfigured { get; }

    string BuildAuthorizationUrl(string redirectUri, string state);

    Task<GmailConnectionInfo> ConnectAsync(
        Guid userId, string code, string redirectUri, CancellationToken cancellationToken = default);

    Task<GmailConnectionInfo?> GetConnectionAsync(Guid userId, CancellationToken cancellationToken = default);

    Task DisconnectAsync(Guid userId, CancellationToken cancellationToken = default);

    Task MarkCheckedAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>An authenticated Gmail client for this user, or null if not connected.</summary>
    Task<GmailService?> GetGmailServiceAsync(Guid userId, CancellationToken cancellationToken = default);
}
