using CareerConnect.Api.Domain;

namespace CareerConnect.Api.Services;

public record SuggestedStatusUpdate(
    Guid ApplicationId,
    string CompanyName,
    string RoleTitle,
    ApplicationStatus CurrentStatus,
    ApplicationStatus SuggestedStatus,
    string Reasoning,
    string EmailSubject,
    string EmailFrom,
    DateTime EmailReceivedAtUtc);

public record SuggestedNewApplication(
    string CompanyName,
    string RoleTitle,
    string Reasoning,
    string EmailSubject,
    string EmailFrom,
    DateTime EmailReceivedAtUtc);

public abstract record GmailScanOutcome
{
    public sealed record Success(
        List<SuggestedStatusUpdate> StatusUpdates,
        List<SuggestedNewApplication> NewApplications) : GmailScanOutcome;
    public sealed record Failed(string Message) : GmailScanOutcome;
}

public interface IGmailUpdateScanner
{
    /// <summary>
    /// Scans recent Gmail for replies about the user's open applications and
    /// returns suggested status changes. Never applies anything itself —
    /// the caller decides what to accept.
    /// </summary>
    Task<GmailScanOutcome> ScanAsync(Guid userId, CancellationToken cancellationToken = default);
}
