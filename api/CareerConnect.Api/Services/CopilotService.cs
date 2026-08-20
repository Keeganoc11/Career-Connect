using CareerConnect.Api.Data;
using CareerConnect.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CareerConnect.Api.Services;

public enum CopilotFailureReason
{
    AnalyzerUnavailable,
    AnalyzerFailed,
}

/// <summary>The model's array index resolved to a real, stable application id.</summary>
public record ResolvedCopilotAction(string Title, string Detail, string Priority, Guid? ApplicationId);

public record ResolvedCopilotInsights(string OverallSummary, List<ResolvedCopilotAction> Actions);

public abstract record CopilotOutcome
{
    public sealed record Success(ResolvedCopilotInsights Insights) : CopilotOutcome;
    public sealed record Failed(CopilotFailureReason Reason, string Message) : CopilotOutcome;
}

public interface ICopilotService
{
    /// <summary>Generates fresh insights over the caller's whole pipeline. Nothing is persisted — this is on-demand, not cached, so it only runs when asked.</summary>
    Task<CopilotOutcome> AnalyzeAsync(Guid userId, CancellationToken cancellationToken = default);
}

public class CopilotService(AppDbContext db, ICopilotAnalyzer analyzer) : ICopilotService
{
    // Rejected/Withdrawn applications are done — same exclusion the Gmail
    // scanner uses, for the same reason: nothing actionable left there.
    private static readonly ApplicationStatus[] ExcludedFromAnalysis =
        [ApplicationStatus.Rejected, ApplicationStatus.Withdrawn];

    public async Task<CopilotOutcome> AnalyzeAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (!analyzer.IsConfigured)
        {
            return new CopilotOutcome.Failed(
                CopilotFailureReason.AnalyzerUnavailable,
                "AI insights need an Anthropic API key. See the README for setup.");
        }

        // Sequential, not concurrent: both queries run on this same
        // request-scoped DbContext, which EF Core does not allow to run more
        // than one operation on at a time.
        var applications = await db.Applications
            .AsNoTracking()
            .Where(a => a.UserId == userId && !ExcludedFromAnalysis.Contains(a.Status))
            .OrderByDescending(a => a.UpdatedAtUtc)
            .ToListAsync(cancellationToken);
        var hasActiveResume = await db.Resumes
            .AsNoTracking()
            .AnyAsync(r => r.UserId == userId && r.IsActive, cancellationToken);

        var latestScores = await db.MatchResults
            .AsNoTracking()
            .Where(m => m.Application.UserId == userId)
            .Where(m => m.CreatedAtUtc == db.MatchResults
                .Where(x => x.ApplicationId == m.ApplicationId)
                .Max(x => x.CreatedAtUtc))
            .Select(m => new { m.ApplicationId, m.Score })
            .ToListAsync(cancellationToken);
        var scoreByApplication = latestScores
            .GroupBy(m => m.ApplicationId)
            .ToDictionary(g => g.Key, g => g.First().Score);

        var snapshot = new CopilotPipelineSnapshot(
            applications
                .Select((a, i) => new CopilotApplicationSnapshot(
                    i,
                    a.CompanyName,
                    a.RoleTitle,
                    a.Status.ToString(),
                    a.DateApplied,
                    a.UpdatedAtUtc,
                    scoreByApplication.GetValueOrDefault(a.Id)))
                .ToList(),
            hasActiveResume,
            DateTime.UtcNow);

        CopilotInsights insights;
        try
        {
            insights = await analyzer.AnalyzeAsync(snapshot, cancellationToken);
        }
        catch (CopilotAnalysisException ex)
        {
            return new CopilotOutcome.Failed(CopilotFailureReason.AnalyzerFailed, ex.Message);
        }

        // Resolve the model's array indices back to real application ids
        // here, in the service layer, rather than trusting the client to
        // line them up against a list it fetched separately.
        var resolvedActions = insights.Actions
            .Select(a => new ResolvedCopilotAction(
                a.Title,
                a.Detail,
                a.Priority,
                a.ApplicationIndex is int i && i >= 0 && i < applications.Count ? applications[i].Id : null))
            .ToList();

        return new CopilotOutcome.Success(new ResolvedCopilotInsights(insights.OverallSummary, resolvedActions));
    }
}
