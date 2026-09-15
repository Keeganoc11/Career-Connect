namespace CareerConnect.Api.Services;

/// <summary>One interview email, with enough of it to find a time in.</summary>
public record InterviewEmailContext(
    int Index, string Subject, string Body, DateTime ReceivedAtUtc, string CompanyName, string RoleTitle);

/// <summary>
/// What an interview email actually pinned down. <see cref="ScheduledAt"/> is
/// null when the email talks about interviewing without naming a time — the
/// common case for "we'd like to schedule a call, what works for you?".
/// </summary>
public record ExtractedInterviewDetails(int Index, DateTimeOffset? ScheduledAt, string Kind);

/// <summary>
/// Second pass over emails already classified as interview-related, reading the
/// body for a date and time.
/// <para>
/// Separate from <see cref="IEmailStatusClassifier"/> because it needs
/// something the first pass deliberately avoids: the full message body. Running
/// it only against confirmed interview matches keeps that to a handful of
/// emails per scan rather than everything the search returned.
/// </para>
/// </summary>
public interface IInterviewDetailsExtractor
{
    /// <summary>False when no API key is configured.</summary>
    bool IsConfigured { get; }

    Task<List<ExtractedInterviewDetails>> ExtractAsync(
        List<InterviewEmailContext> emails, CancellationToken cancellationToken = default);
}
