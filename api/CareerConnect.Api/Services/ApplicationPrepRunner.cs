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
        // Which lines belong to an entry swapped in from extra facts, keyed to
        // its label, so the swap is reported — and put back — as one piece.
        var swaps = new Dictionary<string, string>();
        var budgets = guard.CharacterBudgets(baseLayout);

        // Asking for changes builds on the version you already have, rather
        // than throwing away a rewrite you were mostly happy with.
        var honoringRequest = run.Instructions is not null;
        if (honoringRequest
            && application.TailoredResumeLayout is { } previous
            && SameShape(baseLayout, previous)
            && previous.ToPlainText() != baseLayout.ToPlainText())
        {
            best = previous;
            bestScore = await ScoreAsync(run, resume.Id, previous, usedTailoredResume: true, cancellationToken);
            foreach (var change in await PreviousChangesAsync(run, cancellationToken))
            {
                reasons[change.LineId] = change.Reason;
                if (change.Swap is not null)
                {
                    swaps[change.LineId] = change.Swap;
                }
            }
            await AddStepAsync(run, "Scored your current tailored version", bestScore.Summary, bestScore.Score, cancellationToken);
        }

        // An ordinary pass stops once the resume clears the bar; one you asked
        // for always makes at least one rewrite, whatever the score already is.
        while ((bestScore.Score < run.TargetScore || (honoringRequest && run.Iterations == 0))
               && run.Iterations < MaxIterations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            run.Iterations++;

            // Swaps are decided up front — or when you ask for one — so later
            // passes polish the entry that's there instead of trading entries back and forth.
            var proposal = await tailorer.TailorAsync(
                best, baseLayout, budgets, bestScore, context, run.Instructions,
                allowSwaps: run.Iterations == 1 || honoringRequest, cancellationToken);
            var guarded = await guard.ApplyAsync(best, baseLayout, proposal.Edits, context, proposal.Swaps, cancellationToken);

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

            var swapped = guarded.Swaps.Count == 0
                ? ""
                : $", swapped in {string.Join(" and ", guarded.Swaps.Select(sw => sw.Label.Split(" → ")[^1]))},";
            await AddStepAsync(
                run,
                $"Rewrote {Lines(guarded.Applied.Count)}{swapped} and re-scored (pass {run.Iterations})",
                guarded.Rejected.Count == 0
                    ? candidate.Summary
                    : $"{candidate.Summary} Discarded: {Summarize(guarded.Rejected)}",
                candidate.Score,
                cancellationToken);

            var improved = candidate.Score > bestScore.Score;
            var keepAnyway = honoringRequest && run.Iterations == 1;

            // A tie still keeps the rewrite — it's in the posting's own language,
            // which is worth having even when the score doesn't move. A
            // regression is thrown away, unless it's the change you asked for.
            if (candidate.Score >= bestScore.Score || keepAnyway)
            {
                if (keepAnyway && candidate.Score < bestScore.Score)
                {
                    await AddStepAsync(
                        run,
                        "Kept the change you asked for",
                        $"It scores {candidate.Score}, down from {bestScore.Score}, but it's the version you asked for.",
                        null,
                        cancellationToken);
                }

                best = guarded.Layout;
                bestScore = candidate;
                foreach (var edit in guarded.Applied)
                {
                    reasons[edit.LineId] = edit.Reason;
                }
                foreach (var swap in guarded.Swaps)
                {
                    foreach (var lineId in swap.LineIds)
                    {
                        reasons[lineId] = swap.Reason;
                        swaps[lineId] = swap.Label;
                    }
                }
            }

            // The next pass would rewrite this rewrite, so a pass that didn't
            // improve means the resume has given the posting everything it honestly can.
            if (!improved)
            {
                if (!keepAnyway)
                {
                    await AddStepAsync(
                        run,
                        "Stopped rewriting",
                        "That pass didn't score better than the one before it, so this is as close a fit as your experience supports.",
                        null,
                        cancellationToken);
                }
                break;
            }
        }

        var changes = Diff(baseLayout, best, reasons, swaps);

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
                    // One unsupported line in a swapped entry takes the whole
                    // swap back out — an original bullet under the new heading
                    // would be a new kind of wrong.
                    var lineIds = swaps.TryGetValue(claim.LineId, out var label)
                        ? swaps.Where(pair => pair.Value == label).Select(pair => pair.Key).ToList()
                        : [claim.LineId];
                    foreach (var lineId in lineIds)
                    {
                        best = best.WithLine(baseLayout.Find(lineId)!);
                        swaps.Remove(lineId);
                    }
                }
                best = best.WithLinksFrom(baseLayout);
                changes = Diff(baseLayout, best, reasons, swaps);

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

    /// <summary>
    /// Every line that reads differently from the base resume. Bullets and
    /// skills compare just their words; a swapped heading compares the whole line.
    /// </summary>
    private static List<ResumeChange> Diff(
        ResumeLayout baseLayout,
        ResumeLayout tailored,
        IReadOnlyDictionary<string, string> reasons,
        IReadOnlyDictionary<string, string> swaps) =>
        baseLayout.Lines
            .Select(original => (Original: original, Now: tailored.Find(original.Id)))
            .Where(pair => pair.Now is not null && pair.Original.Text != pair.Now.Text)
            .Select(pair => pair.Original.EditableText is { } before && pair.Now!.EditableText is { } after
                ? new ResumeChange(pair.Original.Id, before, after, reasons.GetValueOrDefault(pair.Original.Id, ""), swaps.GetValueOrDefault(pair.Original.Id))
                : new ResumeChange(pair.Original.Id, pair.Original.Text, pair.Now!.Text, reasons.GetValueOrDefault(pair.Original.Id, ""), swaps.GetValueOrDefault(pair.Original.Id)))
            .ToList();

    /// <summary>
    /// Whether a stored tailored version was built from this base resume, so it
    /// can be built on: the same lines in the same places. Text is allowed to
    /// differ — that's what tailoring changed, swapped entries included.
    /// </summary>
    private static bool SameShape(ResumeLayout baseLayout, ResumeLayout tailored) =>
        baseLayout.Lines.Count == tailored.Lines.Count
        && baseLayout.Lines.Zip(tailored.Lines).All(pair =>
            pair.First.Id == pair.Second.Id
            && pair.First.Kind == pair.Second.Kind
            && pair.First.RowCount == pair.Second.RowCount
            && Math.Abs(pair.First.Baseline - pair.Second.Baseline) < 0.01);

    private async Task<List<ResumeChange>> PreviousChangesAsync(PrepRun run, CancellationToken cancellationToken)
    {
        var previous = await db.PrepRuns
            .AsNoTracking()
            .Where(r => r.ApplicationId == run.ApplicationId && r.Id != run.Id && r.Status == PrepRunStatus.Succeeded)
            .OrderByDescending(r => r.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        return previous?.Changes ?? [];
    }

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
