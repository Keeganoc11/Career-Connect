namespace CareerConnect.Api.Services;

public record CandidateEmail(int Index, string Subject, string From, string Snippet, DateTime ReceivedAtUtc);

public record OpenApplicationContext(int Index, string CompanyName, string RoleTitle, string CurrentStatus);

public record EmailClassificationMatch(int EmailIndex, int ApplicationIndex, string SuggestedStatus, string Reasoning);

public record EmailNewApplicationMatch(int EmailIndex, string CompanyName, string RoleTitle, string Reasoning);

public record EmailClassificationResult(
    List<EmailClassificationMatch> StatusMatches,
    List<EmailNewApplicationMatch> NewApplications);

/// <summary>
/// Matches recent emails against a user's open applications and infers what
/// status each match implies, and separately flags emails that look like
/// confirmations for applications not yet tracked. Isolated behind an
/// interface so scan orchestration is unit-testable without a network call.
/// </summary>
public interface IEmailStatusClassifier
{
    /// <summary>False when no API key is configured.</summary>
    bool IsConfigured { get; }

    Task<EmailClassificationResult> ClassifyAsync(
        List<CandidateEmail> emails,
        List<OpenApplicationContext> openApplications,
        CancellationToken cancellationToken = default);
}
