using CareerConnect.Api.Data;
using CareerConnect.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CareerConnect.Api.Services;

public interface IApplicationPrepRunner
{
    /// <summary>Executes an already-created Running prep run to completion, writing progress as it goes.</summary>
    Task ExecuteAsync(Guid prepRunId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The automated loop: score the active resume against the posting, rewrite and
/// re-score until it clears the target, then write a cover letter against
/// whichever version won. Every step is saved as it completes so the UI can
/// follow along and a closed tab loses nothing.
/// </summary>
public class ApplicationPrepRunner(
    AppDbContext db,
    IResumeMatchAnalyzer analyzer,
    IResumeTailorer tailorer,
    ICoverLetterGenerator coverLetters,
    ILogger<ApplicationPrepRunner> logger) : IApplicationPrepRunner
{
    public async Task ExecuteAsync(Guid prepRunId, CancellationToken cancellationToken = default)
    {
        var run = await db.PrepRuns
            .Include(r => r.Application)
            .FirstOrDefaultAsync(r => r.Id == prepRunId, cancellationToken);

        if (run is null || run.Status != PrepRunStatus.Running)
        {
            return;
        }

        try
        {
            await RunPipelineAsync(run, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Shutdown mid-run. Leave it Running — the worker marks orphaned
            // runs as interrupted on next startup, which reads better than a
            // failure the user didn't cause.
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Prep run {PrepRunId} failed.", prepRunId);
            await FailAsync(run, ex is ResumeTailorException or MatchAnalysisException or CoverLetterGenerationException
                ? ex.Message
                : "Something went wrong while preparing this application.");
        }
    }

    private async Task RunPipelineAsync(PrepRun run, CancellationToken cancellationToken)
    {
        var application = run.Application;

        if (string.IsNullOrWhiteSpace(application.JobDescriptionText))
        {
            await FailAsync(run, "This application has no job description to prepare against.");
            return;
        }

        var resume = await db.Resumes
            .FirstOrDefaultAsync(r => r.UserId == application.UserId && r.IsActive, cancellationToken);

        if (resume is null)
        {
            await FailAsync(run, "Add a resume and mark it active before running prep.");
            return;
        }

        var jobDescription = application.JobDescriptionText;

        var baseline = await analyzer.AnalyzeAsync(
            resume.Content, jobDescription, application.RoleTitle, application.CompanyName, cancellationToken);
        await RecordScoreAsync(run, resume.Id, baseline, usedTailoredResume: false, cancellationToken);

        run.BaselineScore = baseline.Score;
        await AddStepAsync(run, "Scored your resume", baseline.Summary, baseline.Score, cancellationToken);

        var best = baseline;
        string? bestTailoredText = null;

        while (best.Score < run.TargetScore && run.Iterations < MaxIterations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            run.Iterations++;

            var candidateText = await tailorer.TailorAsync(
                bestTailoredText ?? resume.Content,
                jobDescription,
                application.RoleTitle,
                application.CompanyName,
                cancellationToken);

            var candidate = await analyzer.AnalyzeAsync(
                candidateText, jobDescription, application.RoleTitle, application.CompanyName, cancellationToken);
            await RecordScoreAsync(run, resume.Id, candidate, usedTailoredResume: true, cancellationToken);

            await AddStepAsync(
                run,
                $"Rewrote and re-scored (pass {run.Iterations})",
                candidate.Summary,
                candidate.Score,
                cancellationToken);

            var improved = candidate.Score > best.Score;

            // A tie still keeps the rewrite — it's reframed in the posting's own
            // language, which is worth having even when the score doesn't move.
            // Only a regression is thrown away.
            if (candidate.Score >= best.Score)
            {
                best = candidate;
                bestTailoredText = candidateText;
            }

            // The next pass would rewrite this rewrite, so a pass that didn't
            // improve is the signal the resume has given the posting everything
            // it has — continuing just drifts further from the original.
            if (!improved)
            {
                await AddStepAsync(
                    run,
                    "Stopped rewriting",
                    "That pass didn't score better than the previous version, so this is as close a fit as your experience supports.",
                    null,
                    cancellationToken);
                break;
            }
        }

        application.TailoredResumeText = bestTailoredText;
        run.FinalScore = best.Score;
        run.ReadyToApply = best.Score >= run.TargetScore;

        var coverLetter = await coverLetters.GenerateAsync(
            bestTailoredText ?? resume.Content,
            jobDescription,
            application.RoleTitle,
            application.CompanyName,
            cancellationToken);
        application.CoverLetterText = coverLetter;

        await AddStepAsync(
            run,
            "Wrote your cover letter",
            bestTailoredText is null
                ? "Written against your active resume."
                : "Written against the tailored version, so it echoes the same framing.",
            null,
            cancellationToken);

        run.Status = PrepRunStatus.Succeeded;
        run.CompletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task RecordScoreAsync(
        PrepRun run, Guid resumeId, MatchAnalysis analysis, bool usedTailoredResume, CancellationToken cancellationToken)
    {
        db.MatchResults.Add(new MatchResult
        {
            Id = Guid.NewGuid(),
            ApplicationId = run.ApplicationId,
            ResumeId = resumeId,
            Score = analysis.Score,
            Summary = analysis.Summary,
            MatchedKeywords = analysis.MatchedKeywords,
            MissingKeywords = analysis.MissingKeywords,
            Suggestions = analysis.Suggestions,
            ModelId = analysis.ModelId,
            UsedTailoredResume = usedTailoredResume,
            CreatedAtUtc = DateTime.UtcNow,
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task AddStepAsync(
        PrepRun run, string label, string detail, int? score, CancellationToken cancellationToken)
    {
        // Replacing the list rather than mutating it in place: the JSON value
        // comparer snapshots by serializing, and a fresh instance makes the
        // change unambiguous to the change tracker.
        run.Steps = [.. run.Steps, new PrepStep(label, detail, score)];
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task FailAsync(PrepRun run, string message)
    {
        run.Status = PrepRunStatus.Failed;
        run.ErrorMessage = message;
        run.ReadyToApply = false;
        run.CompletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>
    /// Each pass is two model calls, and gains flatten fast — the rewrite can
    /// only reframe experience the resume already has, not add any.
    /// </summary>
    private const int MaxIterations = 3;
}
