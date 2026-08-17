using CareerConnect.Api.Contracts;
using CareerConnect.Api.Data;
using CareerConnect.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CareerConnect.Api.Services;

public abstract record ScoreOutcome
{
    public sealed record Success(MatchResultResponse Result) : ScoreOutcome;
    public sealed record Failed(MatchFailureReason Reason, string Message) : ScoreOutcome;
}

public interface IMatchScoringService
{
    /// <summary>Runs a fresh analysis and stores it.</summary>
    Task<ScoreOutcome> ScoreAsync(Guid userId, Guid applicationId, CancellationToken cancellationToken = default);

    /// <summary>Most recent stored result for an application, if any.</summary>
    Task<MatchResultResponse?> GetLatestAsync(Guid userId, Guid applicationId);

    /// <summary>Latest result per application, for the list view.</summary>
    Task<Dictionary<Guid, MatchResultResponse>> GetLatestForAllAsync(Guid userId);
}

public class MatchScoringService(AppDbContext db, IResumeMatchAnalyzer analyzer) : IMatchScoringService
{
    public async Task<ScoreOutcome> ScoreAsync(
        Guid userId,
        Guid applicationId,
        CancellationToken cancellationToken = default)
    {
        var application = await db.Applications
            .FirstOrDefaultAsync(a => a.UserId == userId && a.Id == applicationId, cancellationToken);

        if (application is null)
        {
            return new ScoreOutcome.Failed(
                MatchFailureReason.ApplicationNotFound,
                "That application no longer exists.");
        }

        if (string.IsNullOrWhiteSpace(application.JobDescriptionText))
        {
            return new ScoreOutcome.Failed(
                MatchFailureReason.NoJobDescription,
                "Paste the job description into this application before scoring it.");
        }

        var resume = await db.Resumes
            .FirstOrDefaultAsync(r => r.UserId == userId && r.IsActive, cancellationToken);

        if (resume is null)
        {
            return new ScoreOutcome.Failed(
                MatchFailureReason.NoActiveResume,
                "Add a resume and mark it active before scoring.");
        }

        if (!analyzer.IsConfigured)
        {
            return new ScoreOutcome.Failed(
                MatchFailureReason.AnalyzerUnavailable,
                "Match scoring needs an Anthropic API key. See the README for setup.");
        }

        MatchAnalysis analysis;
        try
        {
            analysis = await analyzer.AnalyzeAsync(
                resume.Content,
                application.JobDescriptionText,
                application.RoleTitle,
                application.CompanyName,
                cancellationToken);
        }
        catch (MatchAnalysisException ex)
        {
            return new ScoreOutcome.Failed(MatchFailureReason.AnalyzerFailed, ex.Message);
        }

        var result = new MatchResult
        {
            Id = Guid.NewGuid(),
            ApplicationId = application.Id,
            ResumeId = resume.Id,
            Score = analysis.Score,
            Summary = analysis.Summary,
            MatchedKeywords = analysis.MatchedKeywords,
            MissingKeywords = analysis.MissingKeywords,
            Suggestions = analysis.Suggestions,
            ModelId = analysis.ModelId,
            CreatedAtUtc = DateTime.UtcNow,
        };

        db.MatchResults.Add(result);
        await db.SaveChangesAsync(cancellationToken);

        return new ScoreOutcome.Success(ToResponse(result, resume.Label));
    }

    public async Task<MatchResultResponse?> GetLatestAsync(Guid userId, Guid applicationId)
    {
        var latest = await db.MatchResults
            .AsNoTracking()
            .Include(m => m.Resume)
            .Where(m => m.ApplicationId == applicationId && m.Application.UserId == userId)
            .OrderByDescending(m => m.CreatedAtUtc)
            .FirstOrDefaultAsync();

        return latest is null ? null : ToResponse(latest, latest.Resume.Label);
    }

    public async Task<Dictionary<Guid, MatchResultResponse>> GetLatestForAllAsync(Guid userId)
    {
        // Rescoring never overwrites a MatchResult — it adds a new one — so this
        // table only grows. Filtering to each application's latest in SQL keeps
        // this query flat instead of transferring the full rescore history.
        var results = await db.MatchResults
            .AsNoTracking()
            .Include(m => m.Resume)
            .Where(m => m.Application.UserId == userId)
            .Where(m => m.CreatedAtUtc == db.MatchResults
                .Where(x => x.ApplicationId == m.ApplicationId)
                .Max(x => x.CreatedAtUtc))
            .ToListAsync();

        // A GroupBy here is just a tie-break for the (extremely unlikely) case of
        // two results with an identical timestamp — it never runs over more than
        // one extra row per application.
        return results
            .GroupBy(m => m.ApplicationId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var latest = group.OrderByDescending(m => m.CreatedAtUtc).First();
                    return ToResponse(latest, latest.Resume.Label);
                });
    }

    private static MatchResultResponse ToResponse(MatchResult m, string resumeLabel) => new()
    {
        Id = m.Id,
        ApplicationId = m.ApplicationId,
        ResumeId = m.ResumeId,
        ResumeLabel = resumeLabel,
        Score = m.Score,
        Summary = m.Summary,
        MatchedKeywords = m.MatchedKeywords,
        MissingKeywords = m.MissingKeywords,
        Suggestions = m.Suggestions
            .Select(s => new SuggestedEditResponse
            {
                Section = s.Section,
                Guidance = s.Guidance,
                SuggestedText = s.SuggestedText,
            })
            .ToList(),
        ModelId = m.ModelId,
        CreatedAtUtc = m.CreatedAtUtc,
    };
}
