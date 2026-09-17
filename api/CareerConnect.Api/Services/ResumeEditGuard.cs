using System.Text.RegularExpressions;
using CareerConnect.Api.Domain;

namespace CareerConnect.Api.Services;

public record AppliedEdit(string LineId, string Text, string Reason);

public record RejectedEdit(string LineId, string Text, string Why);

/// <summary>An entry swap that passed every check. Its bullets are also listed in <see cref="GuardedEdits.Applied"/>.</summary>
public record AppliedSwap(string SlotId, string Label, string Reason, List<string> LineIds);

public record GuardedEdits(
    ResumeLayout Layout, List<AppliedEdit> Applied, List<RejectedEdit> Rejected, List<AppliedSwap> Swaps);

public interface IResumeEditGuard
{
    /// <summary>
    /// Applies proposed edits to <paramref name="current"/>, keeping only the
    /// ones that pass every rule. A rule a model could talk its way around is
    /// checked here in code instead.
    /// </summary>
    Task<GuardedEdits> ApplyAsync(
        ResumeLayout current,
        ResumeLayout baseLayout,
        IReadOnlyList<LineEdit> edits,
        TailorContext context,
        IReadOnlyList<EntrySwapProposal>? swaps = null,
        CancellationToken cancellationToken = default);

    IReadOnlyDictionary<string, CharacterRange> CharacterBudgets(ResumeLayout baseLayout);
}

public partial class ResumeEditGuard(IResumeRenderer renderer, IResumeLayoutTailorer tailorer) : IResumeEditGuard
{
    /// <summary>Rounds of "that doesn't fit its space, adjust it" before giving up on a line.</summary>
    private const int FitAttempts = 2;

    public IReadOnlyDictionary<string, CharacterRange> CharacterBudgets(ResumeLayout baseLayout) =>
        baseLayout.Lines
            // Locked bullets too (a link line): an entry swap can fill them.
            .Where(l => l.Editable || l.Kind == ResumeLineKind.Bullet)
            .ToDictionary(l => l.Id, l => renderer.CharacterBudget(baseLayout, l));

    public async Task<GuardedEdits> ApplyAsync(
        ResumeLayout current,
        ResumeLayout baseLayout,
        IReadOnlyList<LineEdit> edits,
        TailorContext context,
        IReadOnlyList<EntrySwapProposal>? swaps = null,
        CancellationToken cancellationToken = default)
    {
        var evidence = $"{baseLayout.ToPlainText()}\n{context.ExtraFacts}";
        var allowedNumbers = NumbersIn(evidence);

        var layout = current;
        var applied = new Dictionary<string, AppliedEdit>();
        var rejected = new List<RejectedEdit>();
        var appliedSwaps = new List<AppliedSwap>();
        var swapper = new EntrySwapper(renderer);

        // Swaps first: a swapped entry replaces every line in its slot, so any
        // line edit proposed for those lines is moot.
        foreach (var swap in (swaps ?? []).GroupBy(s => s.SlotId).Select(g => g.Last()))
        {
            var outcome = swapper.Apply(layout, swap, context.ExtraFacts,
                text => CheckText(Clean(text), allowedNumbers));

            if (outcome is SwapOutcome.Rejected no)
            {
                rejected.Add(new RejectedEdit(swap.SlotId, swap.SourceName, $"Swap rejected: {no.Why}"));
                continue;
            }

            var yes = (SwapOutcome.Applied)outcome;
            layout = yes.Layout;
            var reason = swap.Reason.Trim();
            appliedSwaps.Add(new AppliedSwap(swap.SlotId, yes.Label, reason, yes.LineIds));
            foreach (var id in yes.BulletLineIds)
            {
                applied[id] = new AppliedEdit(id, layout.Find(id)!.EditableText!, reason);
            }
        }

        var swappedLines = appliedSwaps.SelectMany(s => s.LineIds).ToHashSet();

        foreach (var edit in edits.Where(e => !swappedLines.Contains(e.LineId)).GroupBy(e => e.LineId).Select(g => g.Last()))
        {
            var line = current.Find(edit.LineId);
            var text = Clean(edit.Text);

            var problem = Check(line, text, allowedNumbers);
            if (problem is not null)
            {
                rejected.Add(new RejectedEdit(edit.LineId, edit.Text, problem));
                continue;
            }

            if (text == line!.EditableText)
            {
                continue;
            }

            layout = layout.WithLine(line.WithEditableText(text));
            applied[edit.LineId] = new AppliedEdit(edit.LineId, text, edit.Reason.Trim());
        }

        // A rewrite must fill exactly the rows its paragraph had: past the
        // margin or onto an extra row and the page changes shape, one row short
        // and it leaves a hole. Misfits go back to be adjusted; what still
        // doesn't fit keeps the words it had.
        var budgets = CharacterBudgets(baseLayout);
        for (var attempt = 0; attempt <= FitAttempts; attempt++)
        {
            var misfits = applied.Keys
                .Select(id => (Id: id, Measurement: Measure(layout, id)))
                .Where(x => x.Measurement is null || !x.Measurement.Fits || x.Measurement.LeavesGap)
                .ToList();

            if (misfits.Count == 0)
            {
                break;
            }

            if (attempt == FitAttempts)
            {
                foreach (var (id, measurement) in misfits)
                {
                    if (!applied.ContainsKey(id))
                    {
                        continue; // Already put back with the rest of its swap.
                    }

                    // A swapped entry goes back whole — one old bullet under a
                    // new heading would be worse than no swap at all.
                    var swap = appliedSwaps.FirstOrDefault(s => s.LineIds.Contains(id));
                    var lineIds = swap?.LineIds ?? [id];
                    foreach (var lineId in lineIds)
                    {
                        layout = layout.WithLine(current.Find(lineId)!);
                        applied.Remove(lineId);
                    }

                    var rows = current.Find(id)!.RowCount;
                    var why = measurement is { LeavesGap: true }
                        ? $"Too short to fill its {rows} lines."
                        : rows == 1 ? "Didn't fit on one line." : $"Didn't fit in its {rows} lines.";

                    if (swap is not null)
                    {
                        appliedSwaps.Remove(swap);
                        rejected.Add(new RejectedEdit(swap.SlotId, swap.Label, $"Swap undone: a bullet {why.ToLowerInvariant()}"));
                    }
                    else
                    {
                        rejected.Add(new RejectedEdit(id, measurement is null ? "" : layout.Find(id)!.Text, why));
                    }
                }
                break;
            }

            var refitted = await tailorer.FitAsync(
                misfits
                    .Select(m => Target(m.Id, layout, budgets, attempt, tooShort: m.Measurement is { LeavesGap: true }))
                    .ToList(),
                context,
                cancellationToken);

            foreach (var edit in refitted.Where(e => applied.ContainsKey(e.LineId)))
            {
                var text = Clean(edit.Text);
                // Checked against the line as it is now: a swapped-in bullet may
                // sit where a locked link line used to be.
                if (Check(layout.Find(edit.LineId), text, allowedNumbers) is not null)
                {
                    continue; // Keep the previous version; it's retried or reverted next round.
                }

                layout = layout.WithLine(layout.Find(edit.LineId)!.WithEditableText(text));
                applied[edit.LineId] = applied[edit.LineId] with { Text = text };
            }
        }

        return new GuardedEdits(layout.WithLinksFrom(baseLayout), applied.Values.ToList(), rejected, appliedSwaps);
    }

    private LineMeasurement? Measure(ResumeLayout layout, string lineId)
    {
        try
        {
            return renderer.MeasureLine(layout, layout.Find(lineId)!);
        }
        catch (ResumeRenderException)
        {
            return null;
        }
    }

    /// <summary>
    /// The range to ask for. Each retry narrows it from the side that just
    /// failed, since the estimate just proved optimistic in that direction.
    /// </summary>
    private LineToFit Target(
        string id, ResumeLayout layout, IReadOnlyDictionary<string, CharacterRange> budgets, int attempt, bool tooShort)
    {
        var line = layout.Find(id)!;
        var range = budgets.TryGetValue(id, out var known) && known.Max > 0
            ? known
            : renderer.CharacterBudget(layout, line);
        var step = 4 * (attempt + 1);
        var min = tooShort ? Math.Min(range.Min + step, range.Max - 1) : range.Min;
        var max = tooShort ? range.Max : Math.Max(range.Max - step, Math.Max(min + 1, 20));
        return new LineToFit(id, line.EditableText!, min, max, line.RowCount, tooShort);
    }

    private static string? Check(ResumeLine? line, string text, HashSet<string> allowedNumbers)
    {
        if (line is null || !line.Editable)
        {
            return "Not an editable line.";
        }

        return CheckText(text, allowedNumbers);
    }

    /// <summary>The rules for words themselves, whichever line they're going onto.</summary>
    private static string? CheckText(string text, HashSet<string> allowedNumbers)
    {
        if (text.Length < 3)
        {
            return "Empty rewrite.";
        }

        if (text.Contains('[') || text.Contains(']'))
        {
            return "Contains a placeholder instead of real content.";
        }

        var invented = NumbersIn(text).Where(n => !allowedNumbers.Contains(n)).ToList();
        if (invented.Count > 0)
        {
            return $"Uses {string.Join(", ", invented)}, which isn't anywhere in your resume.";
        }

        return null;
    }

    /// <summary>
    /// Whitespace collapsed, and anything a model might add around the words —
    /// a bullet glyph, wrapping quotes, markdown bold — stripped off.
    /// </summary>
    private static string Clean(string text)
    {
        var cleaned = Whitespace().Replace(text, " ").Trim();
        cleaned = cleaned.TrimStart('●', '•', '-', '*', '–', ' ').Replace("**", "");
        if (cleaned.Length > 1 && cleaned[0] == '"' && cleaned[^1] == '"')
        {
            cleaned = cleaned[1..^1];
        }
        return cleaned.Trim();
    }

    /// <summary>The numbers in a text, as bare digits — "40%" and "200+" become "40" and "200".</summary>
    private static HashSet<string> NumbersIn(string text) =>
        Number().Matches(text)
            .Select(m => m.Value.Replace(",", "").TrimEnd('.'))
            .ToHashSet();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\d+(?:[.,]\d+)*")]
    private static partial Regex Number();
}
