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
}
