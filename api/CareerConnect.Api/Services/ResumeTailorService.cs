using CareerConnect.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace CareerConnect.Api.Services;

public enum TailorFailureReason
{
    ApplicationNotFound,
    NoJobDescription,
    TailorerUnavailable,
    TailorerFailed,
}

public abstract record TailorOutcome
{
    public sealed record Success(string Content) : TailorOutcome;
    public sealed record Failed(TailorFailureReason Reason, string Message) : TailorOutcome;
}

public interface IResumeTailorService
{
    /// <summary>
    /// Generates tailored resume text for one application. Nothing is
    /// persisted — the caller reviews the result and saves it through the
    /// normal resume create/update endpoints, same as a manual edit.
    /// </summary>
    Task<TailorOutcome> TailorAsync(
        Guid userId, Guid applicationId, string resumeContent, CancellationToken cancellationToken = default);
}

public class ResumeTailorService(AppDbContext db, IResumeTailorer tailorer) : IResumeTailorService
{
    public async Task<TailorOutcome> TailorAsync(
        Guid userId, Guid applicationId, string resumeContent, CancellationToken cancellationToken = default)
    {
        var application = await db.Applications
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.UserId == userId && a.Id == applicationId, cancellationToken);

        if (application is null)
        {
            return new TailorOutcome.Failed(
                TailorFailureReason.ApplicationNotFound, "That application no longer exists.");
        }

        if (string.IsNullOrWhiteSpace(application.JobDescriptionText))
        {
            return new TailorOutcome.Failed(
                TailorFailureReason.NoJobDescription,
                "Paste the job description into this application before tailoring a resume to it.");
        }

        if (!tailorer.IsConfigured)
        {
            return new TailorOutcome.Failed(
                TailorFailureReason.TailorerUnavailable,
                "AI resume tailoring needs an Anthropic API key. See the README for setup.");
        }

        try
        {
            var tailored = await tailorer.TailorAsync(
                resumeContent, application.JobDescriptionText, application.RoleTitle, application.CompanyName, cancellationToken);
            return new TailorOutcome.Success(tailored);
        }
        catch (ResumeTailorException ex)
        {
            return new TailorOutcome.Failed(TailorFailureReason.TailorerFailed, ex.Message);
        }
    }
}
