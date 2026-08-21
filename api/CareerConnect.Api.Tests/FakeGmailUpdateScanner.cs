using CareerConnect.Api.Services;

namespace CareerConnect.Api.Tests;

/// <summary>
/// Stand-in for the real scanner so the scheduled-run orchestration can be
/// tested without a Gmail account or an Anthropic key.
/// </summary>
public sealed class FakeGmailUpdateScanner : IGmailUpdateScanner
{
    /// <summary>Per-user outcome; users not present here get EmptyOutcome.</summary>
    public Dictionary<Guid, GmailScanOutcome> OutcomeByUser { get; } = [];

    public GmailScanOutcome DefaultOutcome { get; set; } = new GmailScanOutcome.Success([], []);

    public List<Guid> ScannedUserIds { get; } = [];

    public Task<GmailScanOutcome> ScanAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        ScannedUserIds.Add(userId);
        return Task.FromResult(OutcomeByUser.GetValueOrDefault(userId, DefaultOutcome));
    }
}
