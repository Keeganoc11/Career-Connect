using CareerConnect.Api.Contracts;
using CareerConnect.Api.Data;
using CareerConnect.Api.Domain;
using Microsoft.EntityFrameworkCore;
using static CareerConnect.Api.Services.ClaudeStructuredCaller;

namespace CareerConnect.Api.Services;

public record FollowUpContext(
    string CandidateName,
    string CompanyName,
    string RoleTitle,
    ApplicationStatus Status,
    DateOnly DateApplied,
    int DaysSilent,
    InterviewEvent? LastInterview,
    DateTime? LastFollowUpAtUtc,
    string? JobDescriptionExcerpt);

public record FollowUpDraft(string Subject, string Body);

public interface IFollowUpWriter
{
    bool IsConfigured { get; }
    Task<FollowUpDraft> WriteAsync(FollowUpContext context, CancellationToken cancellationToken = default);
}

public class ClaudeFollowUpWriter(ClaudeStructuredCaller caller) : IFollowUpWriter
{
    private const string SystemPrompt = """
        Write a short follow-up email from a job candidate who hasn't heard back.

        - 60 to 120 words. Plain text, no markdown, no bracketed placeholders.
        - Warm, direct, confident — never apologetic, needy, or pushy.
        - If they haven't interviewed yet: restate interest in the specific role,
          add one concrete reason they fit drawn from the posting, and ask whether
          there's an update on timing.
        - If they have interviewed: thank them for the conversation, reaffirm
          interest, and ask about next steps.
        - If they already followed up once, keep it even shorter and give an easy
          out ("if the role's been filled, I'd appreciate knowing").
        - Greeting "Hi," (the recipient's name isn't known). Sign off with the
          candidate's name.
        - The subject is short and specific to the role.
        """;

    public bool IsConfigured => caller.IsConfigured;

    public async Task<FollowUpDraft> WriteAsync(FollowUpContext context, CancellationToken cancellationToken = default)
    {
        var userPrompt = $"""
            Candidate: {context.CandidateName}
            Company: {context.CompanyName}
            Role: {context.RoleTitle}
            Current status: {context.Status}
            Applied: {context.DateApplied:MMMM d, yyyy}
            Days without a reply: {context.DaysSilent}
            Last interview: {(context.LastInterview is { } i ? $"{i.Kind} on {i.ScheduledAtUtc:MMMM d}" : "none")}
            Already followed up: {(context.LastFollowUpAtUtc is { } f ? $"yes, on {f:MMMM d}" : "no")}

            <job_posting_excerpt>
            {context.JobDescriptionExcerpt ?? "(not available)"}
            </job_posting_excerpt>
            """;

        var schema = SchemaObject(new
        {
            subject = SchemaString("Short, specific subject line."),
            body = SchemaString("The email body, plain text, with the sign-off."),
        }, "subject", "body");

        var payload = await caller.CallAsync<DraftPayload>(
            "writing your follow-up", SystemPrompt, userPrompt, schema, cancellationToken, maxTokens: 4000);
        return new FollowUpDraft(payload.Subject.Trim(), payload.Body.Trim());
    }

    private sealed record DraftPayload(string Subject, string Body);
}

public abstract record FollowUpOutcome
{
    public sealed record Drafted(FollowUpDraft Draft) : FollowUpOutcome;
    public sealed record NotFound : FollowUpOutcome;
    public sealed record Unavailable(string Message) : FollowUpOutcome;
    public sealed record Failed(string Message) : FollowUpOutcome;
}

public interface IFollowUpService
{
    /// <summary>A draft to copy — nothing is sent. Gmail access is read-only on purpose.</summary>
    Task<FollowUpOutcome> DraftAsync(Guid userId, Guid applicationId, CancellationToken cancellationToken = default);

    /// <summary>Records that the user sent one, which restarts the application's silence clock.</summary>
    Task<ApplicationResponse?> MarkSentAsync(Guid userId, Guid applicationId, CancellationToken cancellationToken = default);
}

public class FollowUpService(AppDbContext db, IFollowUpWriter writer, IApplicationService applications) : IFollowUpService
{
    private const int ExcerptLength = 1500;

    public async Task<FollowUpOutcome> DraftAsync(Guid userId, Guid applicationId, CancellationToken cancellationToken = default)
    {
        var application = await db.Applications
            .AsNoTracking()
            .Include(a => a.Interviews)
            .FirstOrDefaultAsync(a => a.UserId == userId && a.Id == applicationId, cancellationToken);

        if (application is null)
        {
            return new FollowUpOutcome.NotFound();
        }

        if (!writer.IsConfigured)
        {
            return new FollowUpOutcome.Unavailable("Drafting a follow-up needs an Anthropic API key. See the README for setup.");
        }

        // The base resume's first line is the name as the candidate writes it.
        var resume = await db.Resumes.AsNoTracking()
            .Where(r => r.UserId == userId && r.IsActive)
            .FirstOrDefaultAsync(cancellationToken);
        var name = resume?.Layout?.Lines.FirstOrDefault(l => l.Kind != ResumeLineKind.Blank)?.Text
            ?? await db.Users.Where(u => u.Id == userId).Select(u => u.DisplayName).FirstOrDefaultAsync(cancellationToken)
            ?? "";

        var utcNow = DateTime.UtcNow;
        var context = new FollowUpContext(
            name,
            application.CompanyName,
            application.RoleTitle,
            application.Status,
            application.DateApplied,
            (int)(utcNow - application.UpdatedAtUtc).TotalDays,
            application.Interviews.Where(i => i.ScheduledAtUtc < utcNow).MaxBy(i => i.ScheduledAtUtc),
            application.LastFollowUpAtUtc,
            application.JobDescriptionText is { } jd ? jd[..Math.Min(jd.Length, ExcerptLength)] : null);

        try
        {
            return new FollowUpOutcome.Drafted(await writer.WriteAsync(context, cancellationToken));
        }
        catch (ResumeTailoringException ex)
        {
            return new FollowUpOutcome.Failed(ex.Message);
        }
    }

    public async Task<ApplicationResponse?> MarkSentAsync(Guid userId, Guid applicationId, CancellationToken cancellationToken = default)
    {
        var application = await db.Applications
            .FirstOrDefaultAsync(a => a.UserId == userId && a.Id == applicationId, cancellationToken);

        if (application is null)
        {
            return null;
        }

        application.LastFollowUpAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return await applications.GetAsync(userId, applicationId);
    }
}
