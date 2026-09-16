using CareerConnect.Api.Data;
using CareerConnect.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CareerConnect.Api.Services;

public enum PrepStartFailureReason
{
    ApplicationNotFound,
    NoJobDescription,
    NoActiveResume,
    AiUnavailable,
    AlreadyRunning,
}

public abstract record PrepStartOutcome
{
    public sealed record Started(PrepRun Run) : PrepStartOutcome;
    public sealed record Failed(PrepStartFailureReason Reason, string Message) : PrepStartOutcome;
}

public interface IPrepRunService
{
    /// <summary>
    /// Validates preconditions, records a Running pass, and hands it to the
    /// background worker. Returns as soon as the row exists — the pipeline
    /// itself is several chained model calls and finishes long after this.
    /// </summary>
    Task<PrepStartOutcome> StartAsync(Guid userId, Guid applicationId, CancellationToken cancellationToken = default);

    Task<PrepRun?> GetLatestAsync(Guid userId, Guid applicationId, CancellationToken cancellationToken = default);

    /// <summary>Latest run per application, for the list view.</summary>
    Task<Dictionary<Guid, PrepRun>> GetLatestForAllAsync(Guid userId, CancellationToken cancellationToken = default);
}

public class PrepRunService(
    AppDbContext db,
    IPrepRunQueue queue,
    IResumeMatchAnalyzer analyzer,
    IConfiguration configuration) : IPrepRunService
{
    public async Task<PrepStartOutcome> StartAsync(
        Guid userId, Guid applicationId, CancellationToken cancellationToken = default)
    {
        var application = await db.Applications
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.UserId == userId && a.Id == applicationId, cancellationToken);

        if (application is null)
        {
            return new PrepStartOutcome.Failed(
                PrepStartFailureReason.ApplicationNotFound, "That application no longer exists.");
        }

        if (string.IsNullOrWhiteSpace(application.JobDescriptionText))
        {
            return new PrepStartOutcome.Failed(
                PrepStartFailureReason.NoJobDescription,
                "Add the job description to this application before running prep.");
        }

        // One analyzer check stands in for all three model-backed steps: they
        // read the same key, so either everything is configured or nothing is.
        if (!analyzer.IsConfigured)
        {
            return new PrepStartOutcome.Failed(
                PrepStartFailureReason.AiUnavailable,
                "Automated prep needs an Anthropic API key. See the README for setup.");
        }

        var activeResume = await db.Resumes
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.UserId == userId && r.IsActive, cancellationToken);

        if (activeResume is null)
        {
            return new PrepStartOutcome.Failed(
                PrepStartFailureReason.NoActiveResume,
                "Add a resume and mark it active before running prep.");
        }

        if (activeResume.Layout is null)
        {
            return new PrepStartOutcome.Failed(
                PrepStartFailureReason.NoActiveResume,
                "Tailoring keeps your resume's exact format, so it needs your resume as a PDF. " +
                "Upload the PDF on the Resumes page and make it your active resume.");
        }

        var alreadyRunning = await db.PrepRuns
            .AsNoTracking()
            .AnyAsync(r => r.ApplicationId == applicationId && r.Status == PrepRunStatus.Running, cancellationToken);

        if (alreadyRunning)
        {
            return new PrepStartOutcome.Failed(
                PrepStartFailureReason.AlreadyRunning, "Prep is already running for this application.");
        }

        var run = new PrepRun
        {
            Id = Guid.NewGuid(),
            ApplicationId = applicationId,
            Status = PrepRunStatus.Running,
            TargetScore = configuration.GetValue("Prep:TargetScore", 80),
            StartedAtUtc = DateTime.UtcNow,
        };

        db.PrepRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        queue.Enqueue(run.Id);
        return new PrepStartOutcome.Started(run);
    }

    public Task<PrepRun?> GetLatestAsync(
        Guid userId, Guid applicationId, CancellationToken cancellationToken = default) =>
        db.PrepRuns
            .AsNoTracking()
            .Where(r => r.ApplicationId == applicationId && r.Application.UserId == userId)
            .OrderByDescending(r => r.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<Dictionary<Guid, PrepRun>> GetLatestForAllAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var runs = await db.PrepRuns
            .AsNoTracking()
            .Where(r => r.Application.UserId == userId)
            .Where(r => r.StartedAtUtc == db.PrepRuns
                .Where(x => x.ApplicationId == r.ApplicationId)
                .Max(x => x.StartedAtUtc))
            .ToListAsync(cancellationToken);

        return runs
            .GroupBy(r => r.ApplicationId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.StartedAtUtc).First());
    }
}
