using CareerConnect.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace CareerConnect.Api.Services;

public enum CoverLetterFailureReason
{
    ApplicationNotFound,
    NoJobDescription,
    NoActiveResume,
    GeneratorUnavailable,
    GeneratorFailed,
}

public abstract record CoverLetterOutcome
{
    public sealed record Success(string Content) : CoverLetterOutcome;
    public sealed record Failed(CoverLetterFailureReason Reason, string Message) : CoverLetterOutcome;
}

public interface ICoverLetterService
{
    /// <summary>Generates a cover letter for one application against its active resume. Nothing is persisted.</summary>
    Task<CoverLetterOutcome> GenerateAsync(Guid userId, Guid applicationId, CancellationToken cancellationToken = default);
}

public class CoverLetterService(AppDbContext db, ICoverLetterGenerator generator) : ICoverLetterService
{
    public async Task<CoverLetterOutcome> GenerateAsync(
        Guid userId, Guid applicationId, CancellationToken cancellationToken = default)
    {
        var application = await db.Applications
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.UserId == userId && a.Id == applicationId, cancellationToken);

        if (application is null)
        {
            return new CoverLetterOutcome.Failed(
                CoverLetterFailureReason.ApplicationNotFound, "That application no longer exists.");
        }

        if (string.IsNullOrWhiteSpace(application.JobDescriptionText))
        {
            return new CoverLetterOutcome.Failed(
                CoverLetterFailureReason.NoJobDescription,
                "Paste the job description into this application before generating a cover letter.");
        }

        var resume = await db.Resumes
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.UserId == userId && r.IsActive, cancellationToken);

        if (resume is null)
        {
            return new CoverLetterOutcome.Failed(
                CoverLetterFailureReason.NoActiveResume, "Add a resume and mark it active before generating a cover letter.");
        }

        if (!generator.IsConfigured)
        {
            return new CoverLetterOutcome.Failed(
                CoverLetterFailureReason.GeneratorUnavailable,
                "Cover letter generation needs an Anthropic API key. See the README for setup.");
        }

        try
        {
            var content = await generator.GenerateAsync(
                resume.Content, application.JobDescriptionText, application.RoleTitle, application.CompanyName, cancellationToken);
            return new CoverLetterOutcome.Success(content);
        }
        catch (CoverLetterGenerationException ex)
        {
            return new CoverLetterOutcome.Failed(CoverLetterFailureReason.GeneratorFailed, ex.Message);
        }
    }
}
