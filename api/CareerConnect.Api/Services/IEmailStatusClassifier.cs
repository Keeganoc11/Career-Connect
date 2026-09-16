namespace CareerConnect.Api.Services;

/// <summary>
/// MessageId carries no content — it's the handle for fetching this message's
/// body later, which only happens for emails already identified as interview
/// invitations. Defaulted so the classifier and its tests never need it.
/// </summary>
public record CandidateEmail(
    int Index, string Subject, string From, string Snippet, DateTime ReceivedAtUtc, string? MessageId = null);

public record OpenApplicationContext(int Index, string CompanyName, string RoleTitle, string CurrentStatus);

/// <param name="Certainty">
/// "clear" when the email states the outcome outright ("we won't be moving
/// forward", "let's schedule a phone screen"); "likely" when it's implied.
/// Only clear matches are ever applied without review.
/// </param>
public record EmailClassificationMatch(
    int EmailIndex, int ApplicationIndex, string SuggestedStatus, string Reasoning, string Certainty = "likely")
{
    public bool IsClearCut => string.Equals(Certainty, "clear", StringComparison.OrdinalIgnoreCase);
}

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
