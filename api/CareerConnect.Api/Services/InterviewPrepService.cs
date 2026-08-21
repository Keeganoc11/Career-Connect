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
    /// <summary>Generates interview prep for one application against its active resume. Nothing is persisted.</summary>
    Task<InterviewPrepOutcome> GenerateAsync(Guid userId, Guid applicationId, CancellationToken cancellationToken = default);
}

public class InterviewPrepService(AppDbContext db, IInterviewPrepGenerator generator) : IInterviewPrepService
{
    public async Task<InterviewPrepOutcome> GenerateAsync(
        Guid userId, Guid applicationId, CancellationToken cancellationToken = default)
    {
        var application = await db.Applications
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.UserId == userId && a.Id == applicationId, cancellationToken);

        if (application is null)
        {
            return new InterviewPrepOutcome.Failed(
                InterviewPrepFailureReason.ApplicationNotFound, "That application no longer exists.");
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
            return new InterviewPrepOutcome.Success(prep);
        }
        catch (InterviewPrepGenerationException ex)
        {
            return new InterviewPrepOutcome.Failed(InterviewPrepFailureReason.GeneratorFailed, ex.Message);
        }
    }
}
