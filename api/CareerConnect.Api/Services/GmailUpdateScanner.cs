using CareerConnect.Api.Contracts;
using CareerConnect.Api.Data;
using CareerConnect.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CareerConnect.Api.Services;

public class GmailUpdateScanner(
    AppDbContext db,
    IGmailOAuthService oauth,
    IGmailMailReader mailReader,
    IEmailStatusClassifier classifier) : IGmailUpdateScanner
{
    // Rejected/Withdrawn applications are done; scanning for updates on them
    // just adds noise the classifier has to filter back out.
    private static readonly ApplicationStatus[] ExcludedFromScan =
        [ApplicationStatus.Rejected, ApplicationStatus.Withdrawn];

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
            return new GmailScanOutcome.Success([], []);
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

        var statusUpdates = new List<SuggestedStatusUpdate>();
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

            statusUpdates.Add(new SuggestedStatusUpdate(
                application.Id,
                application.CompanyName,
                application.RoleTitle,
                application.Status,
                suggestedStatus,
                match.Reasoning,
                email.Subject,
                email.From,
                email.ReceivedAtUtc));
        }

        // Defense in depth against the model re-reporting a company that's
        // already tracked, or reporting the same new company twice in one
        // scan — either would risk creating a duplicate application.
        var trackedCompanies = applications
            .Select(a => a.CompanyName.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seenCompanies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var newApplications = new List<SuggestedNewApplication>();
        foreach (var candidate in result.NewApplications)
        {
            var email = emails.ElementAtOrDefault(candidate.EmailIndex);
            if (email is null)
            {
                continue;
            }

            var companyName = candidate.CompanyName.Trim();
            if (companyName.Length == 0 || trackedCompanies.Contains(companyName) || !seenCompanies.Add(companyName))
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
            newApplications.Select(ToResponse).ToList());
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
