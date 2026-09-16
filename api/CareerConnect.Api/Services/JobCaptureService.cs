using CareerConnect.Api.Contracts;
using CareerConnect.Api.Data;
using CareerConnect.Api.Domain;
using Microsoft.EntityFrameworkCore;
using static CareerConnect.Api.Services.ClaudeStructuredCaller;

namespace CareerConnect.Api.Services;

public record PostingIdentity(bool IsJobPosting, string CompanyName, string RoleTitle);

public interface IJobPostingIdentifier
{
    bool IsConfigured { get; }

    /// <summary>
    /// Names the company and role in pasted posting text. Deliberately doesn't
    /// rewrite the description: what the user pasted is what gets tailored against.
    /// </summary>
    Task<PostingIdentity> IdentifyAsync(string text, CancellationToken cancellationToken = default);
}

public class ClaudeJobPostingIdentifier(ClaudeStructuredCaller caller) : IJobPostingIdentifier
{
    private const string SystemPrompt = """
        A job seeker pasted text they copied from a job site — usually LinkedIn,
        Indeed, or a company careers page, often with page debris around it
        ("Apply", "Save", "Show more", company follower counts).

        Decide whether it contains a single, specific job posting. If it does,
        give the hiring company's name and the job title exactly as stated.
        If it doesn't (a search results page, a list of many roles, an unrelated
        text), set is_job_posting to false and leave the names empty — never
        guess from fragments.
        """;

    public bool IsConfigured => caller.IsConfigured;

    public async Task<PostingIdentity> IdentifyAsync(string text, CancellationToken cancellationToken = default)
    {
        var schema = SchemaObject(new
        {
            is_job_posting = new { type = "boolean", description = "True only for a single, specific job posting." },
            company_name = SchemaString("The hiring company, as stated. Empty if not a posting."),
            role_title = SchemaString("The job title, as stated. Empty if not a posting."),
        }, "is_job_posting", "company_name", "role_title");

        var payload = await caller.CallAsync<IdentityPayload>(
            "reading the job posting", SystemPrompt, $"<pasted_text>\n{text}\n</pasted_text>", schema, cancellationToken,
            maxTokens: 2000);

        return new PostingIdentity(payload.IsJobPosting, payload.CompanyName.Trim(), payload.RoleTitle.Trim());
    }

    private sealed record IdentityPayload(bool IsJobPosting, string CompanyName, string RoleTitle);
}

public abstract record JobCaptureOutcome
{
    /// <summary>The application exists. Tailoring started unless <paramref name="PrepMessage"/> says why not.</summary>
    public sealed record Captured(ApplicationResponse Application, PrepRun? Run, string? PrepMessage) : JobCaptureOutcome;

    /// <summary>The same company and role are already tracked — probably pasted twice.</summary>
    public sealed record Duplicate(Guid ExistingApplicationId, string CompanyName, string RoleTitle) : JobCaptureOutcome;

    public sealed record Invalid(string Message) : JobCaptureOutcome;
    public sealed record NotAPosting(string Message) : JobCaptureOutcome;
    public sealed record Unavailable(string Message) : JobCaptureOutcome;
    public sealed record Failed(string Message) : JobCaptureOutcome;
}

public interface IJobCaptureService
{
    /// <summary>
    /// The one-step way in: pasted description (or a link) → a tracked
    /// application → tailoring already running. No form.
    /// </summary>
    Task<JobCaptureOutcome> CaptureAsync(Guid userId, CaptureJobRequest request, CancellationToken cancellationToken = default);
}

public class JobCaptureService(
    AppDbContext db,
    IJobPostingIdentifier identifier,
    IJobPostingIngestService ingest,
    IApplicationService applications,
    IPrepRunService prepRuns) : IJobCaptureService
{
    /// <summary>Shorter than this and it's a title or a snippet, not a description worth tailoring against.</summary>
    public const int MinDescriptionLength = 200;

    public async Task<JobCaptureOutcome> CaptureAsync(
        Guid userId, CaptureJobRequest request, CancellationToken cancellationToken = default)
    {
        var text = request.JobDescriptionText?.Trim() ?? "";
        var url = string.IsNullOrWhiteSpace(request.JobPostingUrl) ? null : request.JobPostingUrl.Trim();

        string companyName, roleTitle, description;

        if (text.Length >= MinDescriptionLength)
        {
            if (!identifier.IsConfigured)
            {
                return new JobCaptureOutcome.Unavailable("Tailoring needs an Anthropic API key. See the README for setup.");
            }

            PostingIdentity identity;
            try
            {
                identity = await identifier.IdentifyAsync(text, cancellationToken);
            }
            catch (ResumeTailoringException ex)
            {
                return new JobCaptureOutcome.Failed(ex.Message);
            }

            if (!identity.IsJobPosting || identity.CompanyName.Length == 0 || identity.RoleTitle.Length == 0)
            {
                return new JobCaptureOutcome.NotAPosting(
                    "That doesn't look like a single job posting. Copy the full description of one role and paste it again.");
            }

            (companyName, roleTitle, description) = (identity.CompanyName, identity.RoleTitle, text);
        }
        else if (url is not null && text.Length == 0)
        {
            // Company career sites usually allow this; LinkedIn and Indeed
            // usually don't, which is why pasting is the main way in.
            switch (await ingest.IngestAsync(url, cancellationToken))
            {
                case JobPostingIngestOutcome.Success success:
                    (companyName, roleTitle, description) = (success.CompanyName, success.RoleTitle, success.JobDescriptionText);
                    break;
                case JobPostingIngestOutcome.Failed { Reason: JobPostingIngestFailureReason.ExtractorUnavailable } failed:
                    return new JobCaptureOutcome.Unavailable(failed.Message);
                case JobPostingIngestOutcome.Failed failed:
                    return new JobCaptureOutcome.NotAPosting(
                        $"{failed.Message} Sites like LinkedIn and Indeed block this — paste the description instead.");
                default:
                    return new JobCaptureOutcome.Failed("Couldn't read that link.");
            }
        }
        else
        {
            return new JobCaptureOutcome.Invalid(text.Length == 0
                ? "Paste the job description, or a link to the posting."
                : "That's too short to be a job description. Paste the whole thing — responsibilities and requirements included.");
        }

        companyName = Truncate(companyName, 200);
        roleTitle = Truncate(roleTitle, 200);

        if (!request.AllowDuplicate)
        {
            var existing = await db.Applications
                .AsNoTracking()
                .Where(a => a.UserId == userId
                    && a.CompanyName.ToLower() == companyName.ToLower()
                    && a.RoleTitle.ToLower() == roleTitle.ToLower())
                .Select(a => new { a.Id, a.CompanyName, a.RoleTitle })
                .FirstOrDefaultAsync(cancellationToken);

            if (existing is not null)
            {
                return new JobCaptureOutcome.Duplicate(existing.Id, existing.CompanyName, existing.RoleTitle);
            }
        }

        var application = await applications.CreateAsync(userId, new CreateApplicationRequest
        {
            CompanyName = companyName,
            RoleTitle = roleTitle,
            JobPostingUrl = url,
            Status = ApplicationStatus.Preparing,
            DateApplied = request.LocalDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
            JobDescriptionText = description,
        });

        return await prepRuns.StartAsync(userId, application.Id, instructions: null, cancellationToken) switch
        {
            PrepStartOutcome.Started started => new JobCaptureOutcome.Captured(application, started.Run, null),
            PrepStartOutcome.Failed failed => new JobCaptureOutcome.Captured(application, null, failed.Message),
            _ => new JobCaptureOutcome.Captured(application, null, "Tailoring couldn't start."),
        };
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
