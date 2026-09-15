using CareerConnect.Api.Contracts;
using CareerConnect.Api.Domain;

namespace CareerConnect.Api.Services;

/// <summary>A candidate status change for one already-tracked application, found during a scan.</summary>
public record SuggestedStatusUpdate(
    Guid ApplicationId,
    string CompanyName,
    string RoleTitle,
    ApplicationStatus CurrentStatus,
    ApplicationStatus SuggestedStatus,
    string Reasoning,
    string EmailSubject,
    string EmailFrom,
    DateTime EmailReceivedAtUtc,
    /// <summary>The time the email named, if it named one. Null is the common case.</summary>
    DateTime? InterviewAtUtc = null,
    InterviewKind? InterviewKind = null);

/// <summary>A candidate new application found during a scan, not yet tracked.</summary>
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
        List<SuggestedStatusUpdateResponse> StatusUpdates,
        List<SuggestedNewApplicationResponse> NewApplications,
        List<AutoAppliedResponse> AutoApplied) : GmailScanOutcome;
    public sealed record Failed(string Message) : GmailScanOutcome;
}

public interface IGmailUpdateScanner
{
    /// <summary>
    /// Scans recent Gmail for replies about the user's open applications and
    /// returns suggested status changes for the caller to accept or dismiss.
    ///
    /// The one exception is Preparing → Applied: an application the user was
    /// prepping, plus a confirmation email from that company, is the user's own
    /// decision coming back as fact. Those are applied here and reported under
    /// AutoApplied. Every other transition stays a suggestion.
    /// </summary>
    Task<GmailScanOutcome> ScanAsync(Guid userId, CancellationToken cancellationToken = default);
}
