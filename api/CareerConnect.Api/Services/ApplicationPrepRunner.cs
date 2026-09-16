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
/// The tailoring loop. Score the base resume against the posting, rewrite its
/// editable lines and re-score until it clears the target, check every change
/// against the candidate's real experience, then write the reality check.
///
/// The resume only ever changes through <see cref="IResumeEditGuard"/>, so
/// whatever a model proposes, the result keeps the uploaded PDF's exact format,
/// stays one page, and every line still fits on its line. Every step is saved
/// as it completes so the UI can follow along and a closed tab loses nothing.
/// </summary>
public class ApplicationPrepRunner(
    AppDbContext db,
    IResumeMatchAnalyzer analyzer,
    IResumeLayoutTailorer tailorer,
    IResumeEditGuard guard,
    IResumeClaimsAuditor auditor,
    IResumeReviewer reviewer,
    ILogger<ApplicationPrepRunner> logger) : IApplicationPrepRunner
{
    /// <summary>
    /// Each pass is at least two model calls, and gains flatten fast — the
    /// rewrite can only reframe experience the resume already has, not add any.
    /// </summary>
    private const int MaxIterations = 3;

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
            await FailAsync(run, ex is ResumeTailoringException or MatchAnalysisException or ResumeRenderException
                ? ex.Message
                : "Something went wrong while tailoring this resume.");
        }
    }

    private async Task RunPipelineAsync(PrepRun run, CancellationToken cancellationToken)
    {
        var application = run.Application;

        if (string.IsNullOrWhiteSpace(application.JobDescriptionText))
        {
            await FailAsync(run, "This application has no job description to tailor against.");
            return;
        }

        var resume = await db.Resumes
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.UserId == application.UserId && r.IsActive, cancellationToken);

        if (resume is null)
        {
            await FailAsync(run, "Add a resume and mark it active before running prep.");
            return;
        }

        if (resume.Layout is null)
        {
            await FailAsync(run,
                "Tailoring needs your resume as a PDF so it can keep its exact format. " +
                "Upload the PDF on the Resumes page and make it your active resume.");
            return;
        }

        var baseLayout = resume.Layout;
        var context = new TailorContext(
            application.JobDescriptionText, application.RoleTitle, application.CompanyName, resume.ExtraFacts);

        var baseline = await ScoreAsync(run, resume.Id, baseLayout, usedTailoredResume: false, cancellationToken);
        run.BaselineScore = baseline.Score;
        await AddStepAsync(run, "Scored your resume", baseline.Summary, baseline.Score, cancellationToken);

        var best = baseLayout;
        var bestScore = baseline;
        var reasons = new Dictionary<string, string>();
        var budgets = guard.CharacterBudgets(baseLayout);

        while (bestScore.Score < run.TargetScore && run.Iterations < MaxIterations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            run.Iterations++;

            var proposals = await tailorer.TailorAsync(best, baseLayout, budgets, bestScore, context, cancellationToken);
            var guarded = await guard.ApplyAsync(best, baseLayout, proposals, context, cancellationToken);

            if (guarded.Applied.Count == 0)
            {
                await AddStepAsync(
                    run,
                    "Nothing left to rewrite",
                    guarded.Rejected.Count == 0
                        ? "No further change would honestly improve the fit."
                        : $"The remaining ideas didn't hold up: {Summarize(guarded.Rejected)}",
                    null,
                    cancellationToken);
                break;
            }

            var candidate = await ScoreAsync(run, resume.Id, guarded.Layout, usedTailoredResume: true, cancellationToken);

            await AddStepAsync(
                run,
                $"Rewrote {Lines(guarded.Applied.Count)} and re-scored (pass {run.Iterations})",
                guarded.Rejected.Count == 0
                    ? candidate.Summary
                    : $"{candidate.Summary} Discarded: {Summarize(guarded.Rejected)}",
                candidate.Score,
                cancellationToken);

            var improved = candidate.Score > bestScore.Score;

            // A tie still keeps the rewrite — it's in the posting's own language,
            // which is worth having even when the score doesn't move. Only a
            // regression is thrown away.
            if (candidate.Score >= bestScore.Score)
            {
                best = guarded.Layout;
                bestScore = candidate;
                foreach (var edit in guarded.Applied)
                {
                    reasons[edit.LineId] = edit.Reason;
                }
            }

            // The next pass would rewrite this rewrite, so a pass that didn't
            // improve means the resume has given the posting everything it honestly can.
            if (!improved)
            {
                await AddStepAsync(
                    run,
                    "Stopped rewriting",
                    "That pass didn't score better than the one before it, so this is as close a fit as your experience supports.",
                    null,
                    cancellationToken);
                break;
            }
        }

        var changes = Diff(baseLayout, best, reasons);

        if (changes.Count > 0)
        {
            var unsupported = (await auditor.AuditAsync(baseLayout, resume.ExtraFacts, changes, cancellationToken))
                .Where(u => changes.Any(c => c.LineId == u.LineId))
                .GroupBy(u => u.LineId)
                .Select(g => g.First())
                .ToList();

            if (unsupported.Count == 0)
            {
                await AddStepAsync(
                    run,
                    "Checked every change against your real experience",
                    "Nothing claims more than your resume backs up.",
                    null,
                    cancellationToken);
            }
            else
            {
                foreach (var claim in unsupported)
                {
                    best = best.WithLine(baseLayout.Find(claim.LineId)!);
                }
                changes = Diff(baseLayout, best, reasons);

                bestScore = await ScoreAsync(run, resume.Id, best, usedTailoredResume: changes.Count > 0, cancellationToken);
                await AddStepAsync(
                    run,
                    $"Put back {Lines(unsupported.Count)} that overstated your experience",
                    string.Join(" ", unsupported.Select(u => $"{u.LineId}: {u.Reason}.")),
                    bestScore.Score,
                    cancellationToken);
            }
        }

        run.FinalScore = bestScore.Score;

        var draft = await reviewer.ReviewAsync(
            best.ToPlainText(), baseline.Score, bestScore.Score, run.TargetScore, context, cancellationToken);

        var verdict = FitVerdicts.From(bestScore.Score, draft.Dealbreakers.Count);
        run.Review = new ResumeReview
        {
            Verdict = verdict,
            RealityCheck = draft.RealityCheck,
            ScoreCeiling = draft.ScoreCeiling,
            Dealbreakers = draft.Dealbreakers,
            Strengths = draft.Strengths,
            Gaps = draft.Gaps,
            WorkOn = draft.WorkOn,
        };
        run.Changes = changes;
        run.ReadyToApply = bestScore.Score >= run.TargetScore && draft.Dealbreakers.Count == 0;

        // Always stored, even untouched: the download is drawn from this, and
        // "your resume already fits" still needs a file to upload.
        application.TailoredResumeLayout = best;
        application.TailoredResumeText = best.ToPlainText();

        await AddStepAsync(run, "Wrote your reality check", draft.RealityCheck, null, cancellationToken);

        run.Status = PrepRunStatus.Succeeded;
        run.CompletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<MatchAnalysis> ScoreAsync(
        PrepRun run, Guid resumeId, ResumeLayout layout, bool usedTailoredResume, CancellationToken cancellationToken)
    {
        var application = run.Application;
        var analysis = await analyzer.AnalyzeAsync(
            layout.ToPlainText(),
            application.JobDescriptionText!,
            application.RoleTitle,
            application.CompanyName,
            cancellationToken);

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
        return analysis;
    }

    private static List<ResumeChange> Diff(
        ResumeLayout baseLayout, ResumeLayout tailored, IReadOnlyDictionary<string, string> reasons) =>
        baseLayout.Lines
            .Where(l => l.Editable)
            .Select(l => (Before: l.EditableText!, After: tailored.Find(l.Id)?.EditableText ?? l.EditableText!, l.Id))
            .Where(x => x.Before != x.After)
            .Select(x => new ResumeChange(x.Id, x.Before, x.After, reasons.GetValueOrDefault(x.Id, "")))
            .ToList();

    private static string Lines(int count) => count == 1 ? "1 line" : $"{count} lines";

    private static string Summarize(IReadOnlyList<RejectedEdit> rejected) =>
        string.Join(" ", rejected.Take(4).Select(r => $"{r.LineId} — {r.Why}"));

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
}
