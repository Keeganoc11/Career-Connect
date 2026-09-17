using CareerConnect.Api.Contracts;
using CareerConnect.Api.Data;
using CareerConnect.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CareerConnect.Api.Services;

public class GmailUpdateScanner(
    AppDbContext db,
    IGmailOAuthService oauth,
    IGmailMailReader mailReader,
    IEmailStatusClassifier classifier,
    IInterviewDetailsExtractor interviewExtractor,
    IApplicationAutomation automation) : IGmailUpdateScanner
{
    // Rejected/Withdrawn applications are done; scanning for updates on them
    // just adds noise the classifier has to filter back out.
    private static readonly ApplicationStatus[] ExcludedFromScan =
        [ApplicationStatus.Rejected, ApplicationStatus.Withdrawn];

    /// <summary>The statuses whose emails are worth opening to look for a time.</summary>
    private static readonly ApplicationStatus[] InterviewStatuses =
        [ApplicationStatus.PhoneScreen, ApplicationStatus.Interview];

    public async Task<GmailScanOutcome> ScanAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var connection = await oauth.GetConnectionAsync(userId, cancellationToken);
        if (connection is null)
        {
            return new GmailScanOutcome.Failed("Connect Gmail before checking for updates.");
        }

        if (!classifier.IsConfigured)
        {
            return new GmailScanOutcome.Failed(
                "Email status detection needs an Anthropic API key. See the README for setup.");
        }

        // Sequential, not concurrent: GetRecentCandidateEmailsAsync internally
        // re-fetches the Gmail connection via oauth.GetGmailServiceAsync,
        // which runs on this same request-scoped DbContext — running it
        // alongside the query below would be two overlapping operations on
        // one DbContext, which EF Core does not allow.
        var applications = await db.Applications
            .AsNoTracking()
            .Where(a => a.UserId == userId && !ExcludedFromScan.Contains(a.Status))
            .ToListAsync(cancellationToken);

        List<CandidateEmail> emails;
        try
        {
            emails = await mailReader.GetRecentCandidateEmailsAsync(userId, connection.LastCheckedAtUtc, cancellationToken);
        }
        catch (Exception ex)
        {
            return new GmailScanOutcome.Failed($"Couldn't read Gmail: {ex.Message}");
        }

        // Advance the watermark regardless of what we found — otherwise a
        // scan with zero matches would keep re-fetching the same old mail.
        await oauth.MarkCheckedAsync(userId, cancellationToken);

        if (emails.Count == 0)
        {
            return new GmailScanOutcome.Success([], [], []);
        }

        // An empty tracker is a valid state now — it just means every
        // candidate email is a possible *new* application, not a status
        // update, since there's nothing yet to match status changes against.
        var appContexts = applications
            .Select((a, i) => new OpenApplicationContext(i, a.CompanyName, a.RoleTitle, a.Status.ToString()))
            .ToList();

        EmailClassificationResult result;
        try
        {
            result = await classifier.ClassifyAsync(emails, appContexts, cancellationToken);
        }
        catch (Exception ex)
        {
            return new GmailScanOutcome.Failed($"Email classification failed: {ex.Message}");
        }

        // Every usable match becomes a candidate first, so the interview pass
        // can read times for all of them — a clear-cut invite that's applied
        // automatically should put its interview on the calendar too.
        var candidates = new List<SuggestedStatusUpdate>();
        var clearCut = new List<bool>();
        var interviewCandidates = new Dictionary<int, CandidateEmail>();
        foreach (var match in result.StatusMatches)
        {
            var email = emails.ElementAtOrDefault(match.EmailIndex);
            var application = applications.ElementAtOrDefault(match.ApplicationIndex);
            if (email is null || application is null)
            {
                continue; // Malformed index from the model — skip rather than throw.
            }

            if (!Enum.TryParse<ApplicationStatus>(match.SuggestedStatus, out var suggestedStatus))
            {
                continue;
            }

            if (suggestedStatus == application.Status)
            {
                continue; // Already reflects this status — nothing to suggest.
            }

            candidates.Add(new SuggestedStatusUpdate(
                application.Id,
                application.CompanyName,
                application.RoleTitle,
                application.Status,
                suggestedStatus,
                match.Reasoning,
                email.Subject,
                email.From,
                email.ReceivedAtUtc));
            clearCut.Add(match.IsClearCut);
            interviewCandidates[candidates.Count - 1] = email;
        }

        await AttachInterviewTimesAsync(userId, candidates, interviewCandidates, cancellationToken);

        var statusUpdates = new List<SuggestedStatusUpdate>();
        var autoApplied = new List<AutoAppliedResponse>();
        var handled = new HashSet<Guid>();

        // Newest email first, so if two emails about one application both
        // qualify, the latest word is the one that's applied.
        foreach (var (candidate, index) in candidates.Select((c, i) => (c, i)).OrderByDescending(x => x.c.EmailReceivedAtUtc))
        {
            if (!handled.Contains(candidate.ApplicationId)
                && AutoApplyPolicy.ShouldApply(candidate.CurrentStatus, candidate.SuggestedStatus, clearCut[index]))
            {
                var activity = await automation.ApplyFromEmailAsync(
                    userId,
                    candidate.ApplicationId,
                    candidate.SuggestedStatus,
                    new EmailEvidence(candidate.Reasoning, candidate.EmailSubject, candidate.EmailFrom, candidate.EmailReceivedAtUtc),
                    candidate is { InterviewAtUtc: { } at, InterviewKind: { } kind } ? (at, kind) : null,
                    cancellationToken);

                if (activity is not null)
                {
                    handled.Add(candidate.ApplicationId);
                    autoApplied.Add(new AutoAppliedResponse
                    {
                        ApplicationId = candidate.ApplicationId,
                        ActivityId = activity.Id,
                        CompanyName = candidate.CompanyName,
                        RoleTitle = candidate.RoleTitle,
                        FromStatus = activity.FromStatus,
                        ToStatus = activity.ToStatus,
                        Reasoning = candidate.Reasoning,
                        EmailSubject = candidate.EmailSubject,
                        EmailFrom = candidate.EmailFrom,
                        EmailReceivedAtUtc = candidate.EmailReceivedAtUtc,
                    });
                    continue;
                }
            }

            // An older email about something already applied automatically is
            // stale by definition — the newer one settled it.
            if (!handled.Contains(candidate.ApplicationId))
            {
                statusUpdates.Add(candidate);
            }
        }

        // Defense in depth against the model re-reporting a company that's
        // already tracked, or reporting the same new company twice in one
        // scan — either would risk creating a duplicate application.

        var newApplications = new List<SuggestedNewApplication>();
        foreach (var candidate in result.NewApplications)
        {
            var email = emails.ElementAtOrDefault(candidate.EmailIndex);
            if (email is null)
            {
                continue;
            }

            var companyName = candidate.CompanyName.Trim();
            if (companyName.Length == 0
                || applications.Any(a => CompanyNames.Same(a.CompanyName, companyName))
                || newApplications.Any(n => CompanyNames.Same(n.CompanyName, companyName)
                                         && CompanyNames.SameRole(n.RoleTitle, candidate.RoleTitle)))
            {
                continue;
            }

            newApplications.Add(new SuggestedNewApplication(
                companyName,
                candidate.RoleTitle.Trim(),
                candidate.Reasoning,
                email.Subject,
                email.From,
                email.ReceivedAtUtc));
        }

        return new GmailScanOutcome.Success(
            statusUpdates.Select(ToResponse).ToList(),
            newApplications.Select(ToResponse).ToList(),
            autoApplied);
    }

    /// <summary>
    /// Second pass: for suggestions that point at an interview, open those
    /// emails and read the scheduled time out of the body. Nothing is written —
    /// the time rides on the suggestion so the user can correct it before
    /// accepting, since a misread time books the wrong appointment.
    /// </summary>
    private async Task AttachInterviewTimesAsync(
        Guid userId,
        List<SuggestedStatusUpdate> statusUpdates,
        Dictionary<int, CandidateEmail> emailsBySuggestion,
        CancellationToken cancellationToken)
    {
        if (!interviewExtractor.IsConfigured)
        {
            return;
        }

        var targets = emailsBySuggestion
            .Where(pair => InterviewStatuses.Contains(statusUpdates[pair.Key].SuggestedStatus)
                        && pair.Value.MessageId is not null)
            .ToList();

        if (targets.Count == 0)
        {
            return;
        }

        Dictionary<string, string> bodies;
        try
        {
            bodies = await mailReader.GetBodiesAsync(
                userId, targets.Select(t => t.Value.MessageId!).Distinct().ToList(), cancellationToken);
        }
        catch (Exception)
        {
            // A status suggestion without a time is still worth showing.
            return;
        }

        // The extractor keys its answers by the index it was given, so pass the
        // suggestion's own index and read the results straight back onto it.
        var contexts = targets
            .Where(t => bodies.ContainsKey(t.Value.MessageId!))
            .Select(t => new InterviewEmailContext(
                t.Key,
                t.Value.Subject,
                bodies[t.Value.MessageId!],
                t.Value.ReceivedAtUtc,
                statusUpdates[t.Key].CompanyName,
                statusUpdates[t.Key].RoleTitle))
            .ToList();

        if (contexts.Count == 0)
        {
            return;
        }

        var extracted = await interviewExtractor.ExtractAsync(contexts, cancellationToken);

        foreach (var details in extracted)
        {
            if (details.ScheduledAt is not { } scheduledAt
                || details.Index < 0
                || details.Index >= statusUpdates.Count)
            {
                continue;
            }

            var kind = Enum.TryParse<InterviewKind>(details.Kind, out var parsed) ? parsed : InterviewKind.Other;
            statusUpdates[details.Index] = statusUpdates[details.Index] with
            {
                InterviewAtUtc = scheduledAt.UtcDateTime,
                InterviewKind = kind,
            };
        }
    }

    private static SuggestedStatusUpdateResponse ToResponse(SuggestedStatusUpdate s) => new()
    {
        ApplicationId = s.ApplicationId,
        CompanyName = s.CompanyName,
        RoleTitle = s.RoleTitle,
        CurrentStatus = s.CurrentStatus,
        SuggestedStatus = s.SuggestedStatus,
        Reasoning = s.Reasoning,
        EmailSubject = s.EmailSubject,
        EmailFrom = s.EmailFrom,
        EmailReceivedAtUtc = s.EmailReceivedAtUtc,
        InterviewAtUtc = s.InterviewAtUtc,
        InterviewKind = s.InterviewKind,
    };

    private static SuggestedNewApplicationResponse ToResponse(SuggestedNewApplication n) => new()
    {
        CompanyName = n.CompanyName,
        RoleTitle = n.RoleTitle,
        Reasoning = n.Reasoning,
        EmailSubject = n.EmailSubject,
        EmailFrom = n.EmailFrom,
        EmailReceivedAtUtc = n.EmailReceivedAtUtc,
    };
}
