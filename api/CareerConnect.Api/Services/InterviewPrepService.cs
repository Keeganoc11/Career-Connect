using System.Text.Json;
using CareerConnect.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace CareerConnect.Api.Services;

public enum InterviewPrepFailureReason
{
    ApplicationNotFound,
    NoJobDescription,
    NoActiveResume,
    GeneratorUnavailable,
    GeneratorFailed,
}

public abstract record InterviewPrepOutcome
{
    public sealed record Success(InterviewPrep Prep) : InterviewPrepOutcome;
    public sealed record Failed(InterviewPrepFailureReason Reason, string Message) : InterviewPrepOutcome;
}

public interface IInterviewPrepService
{
    /// <summary>
    /// Interview prep for one application, generated against the active resume
    /// and stored on the application. Returns the saved copy unless
    /// <paramref name="regenerate"/> forces a fresh pass.
    /// </summary>
    Task<InterviewPrepOutcome> GenerateAsync(
        Guid userId, Guid applicationId, bool regenerate = false, CancellationToken cancellationToken = default);

    /// <summary>The stored prep, or null if none has been generated yet. No model call.</summary>
    Task<InterviewPrep?> GetStoredAsync(Guid userId, Guid applicationId, CancellationToken cancellationToken = default);
}

public class InterviewPrepService(AppDbContext db, IInterviewPrepGenerator generator) : IInterviewPrepService
{
    public async Task<InterviewPrep?> GetStoredAsync(
        Guid userId, Guid applicationId, CancellationToken cancellationToken = default)
    {
        var json = await db.Applications
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.Id == applicationId)
            .Select(a => a.InterviewPrepJson)
            .FirstOrDefaultAsync(cancellationToken);

        return json is null ? null : Deserialize(json);
    }

    public async Task<InterviewPrepOutcome> GenerateAsync(
        Guid userId, Guid applicationId, bool regenerate = false, CancellationToken cancellationToken = default)
    {
        // Tracked, not AsNoTracking: the generated prep is saved back onto this row.
        var application = await db.Applications
            .FirstOrDefaultAsync(a => a.UserId == userId && a.Id == applicationId, cancellationToken);

        if (application is null)
        {
            return new InterviewPrepOutcome.Failed(
                InterviewPrepFailureReason.ApplicationNotFound, "That application no longer exists.");
        }

        // Prep costs a model call and doesn't go stale on its own — serve what
        // was generated before unless the caller explicitly wants it redone.
        if (!regenerate && application.InterviewPrepJson is not null)
        {
            var stored = Deserialize(application.InterviewPrepJson);
            if (stored is not null)
            {
                return new InterviewPrepOutcome.Success(stored);
            }
        }

        if (string.IsNullOrWhiteSpace(application.JobDescriptionText))
        {
            return new InterviewPrepOutcome.Failed(
                InterviewPrepFailureReason.NoJobDescription,
                "Paste the job description into this application before generating interview prep.");
        }

        var resume = await db.Resumes
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.UserId == userId && r.IsActive, cancellationToken);

        if (resume is null)
        {
            return new InterviewPrepOutcome.Failed(
                InterviewPrepFailureReason.NoActiveResume, "Add a resume and mark it active before generating interview prep.");
        }

        if (!generator.IsConfigured)
        {
            return new InterviewPrepOutcome.Failed(
                InterviewPrepFailureReason.GeneratorUnavailable,
                "Interview prep needs an Anthropic API key. See the README for setup.");
        }

        try
        {
            var prep = await generator.GenerateAsync(
                resume.Content, application.JobDescriptionText, application.RoleTitle, application.CompanyName, cancellationToken);

            application.InterviewPrepJson = JsonSerializer.Serialize(prep, SerializerOptions);
            application.InterviewPrepGeneratedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            return new InterviewPrepOutcome.Success(prep);
        }
        catch (InterviewPrepGenerationException ex)
        {
            return new InterviewPrepOutcome.Failed(InterviewPrepFailureReason.GeneratorFailed, ex.Message);
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>
    /// Null rather than throwing on unreadable JSON: a row written by an older
    /// shape should cost one regeneration, not break the endpoint.
    /// </summary>
    private static InterviewPrep? Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<InterviewPrep>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
