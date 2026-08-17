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
    DateTime EmailReceivedAtUtc);

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
        List<SuggestedNewApplicationResponse> NewApplications) : GmailScanOutcome;
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
