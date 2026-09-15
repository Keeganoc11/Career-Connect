namespace CareerConnect.Api.Services;

/// <summary>
/// Pulls candidate emails out of a user's Gmail. Kept free of Google SDK
/// types in its signature so scan orchestration is testable without them.
/// </summary>
public interface IGmailMailReader
{
    /// <param name="after">Only mail received after this point; a generous default lookback is used on first scan.</param>
    Task<List<CandidateEmail>> GetRecentCandidateEmailsAsync(
        Guid userId, DateTime? after, CancellationToken cancellationToken = default);

    /// <summary>
    /// Full plain-text bodies for specific messages, keyed by message id.
    /// <para>
    /// Deliberately separate from the scan: bodies are only fetched for emails
    /// already identified as interview invitations, where the scheduled time
    /// lives in the body and nowhere else. Missing or unreadable messages are
    /// simply absent from the result.
    /// </para>
    /// </summary>
    Task<Dictionary<string, string>> GetBodiesAsync(
        Guid userId, IReadOnlyCollection<string> messageIds, CancellationToken cancellationToken = default);
}
