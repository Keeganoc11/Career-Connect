using System.Text.RegularExpressions;
using CareerConnect.Api.Domain;

namespace CareerConnect.Api.Services;

public record AppliedEdit(string LineId, string Text, string Reason);

public record RejectedEdit(string LineId, string Text, string Why);

public record GuardedEdits(ResumeLayout Layout, List<AppliedEdit> Applied, List<RejectedEdit> Rejected);

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
        CancellationToken cancellationToken = default);

    IReadOnlyDictionary<string, CharacterRange> CharacterBudgets(ResumeLayout baseLayout);
}

public partial class ResumeEditGuard(IResumeRenderer renderer, IResumeLayoutTailorer tailorer) : IResumeEditGuard
{
    /// <summary>Rounds of "that doesn't fit its space, adjust it" before giving up on a line.</summary>
    private const int FitAttempts = 2;

    public IReadOnlyDictionary<string, CharacterRange> CharacterBudgets(ResumeLayout baseLayout) =>
        baseLayout.Lines
            .Where(l => l.Editable)
            .ToDictionary(l => l.Id, l => renderer.CharacterBudget(baseLayout, l));

    public async Task<GuardedEdits> ApplyAsync(
        ResumeLayout current,
        ResumeLayout baseLayout,
        IReadOnlyList<LineEdit> edits,
        TailorContext context,
        CancellationToken cancellationToken = default)
    {
        var evidence = $"{baseLayout.ToPlainText()}\n{context.ExtraFacts}";
        var allowedNumbers = NumbersIn(evidence);

        var layout = current;
        var applied = new Dictionary<string, AppliedEdit>();
        var rejected = new List<RejectedEdit>();

        foreach (var edit in edits.GroupBy(e => e.LineId).Select(g => g.Last()))
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
                    var rows = current.Find(id)!.RowCount;
                    layout = layout.WithLine(current.Find(id)!);
                    rejected.Add(new RejectedEdit(id, applied[id].Text, measurement is { LeavesGap: true }
                        ? $"Too short to fill its {rows} lines."
                        : rows == 1 ? "Didn't fit on one line." : $"Didn't fit in its {rows} lines."));
                    applied.Remove(id);
                }
                break;
            }

            var refitted = await tailorer.FitAsync(
                misfits
                    .Select(m => Target(m.Id, layout.Find(m.Id)!, budgets, attempt, tooShort: m.Measurement is { LeavesGap: true }))
                    .ToList(),
                context,
                cancellationToken);

            foreach (var edit in refitted.Where(e => applied.ContainsKey(e.LineId)))
            {
                var text = Clean(edit.Text);
                if (Check(current.Find(edit.LineId), text, allowedNumbers) is not null)
                {
                    continue; // Keep the previous version; it's retried or reverted next round.
                }

                layout = layout.WithLine(current.Find(edit.LineId)!.WithEditableText(text));
                applied[edit.LineId] = applied[edit.LineId] with { Text = text };
            }
        }

        return new GuardedEdits(layout, applied.Values.ToList(), rejected);
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
    private static LineToFit Target(
        string id, ResumeLine line, IReadOnlyDictionary<string, CharacterRange> budgets, int attempt, bool tooShort)
    {
        var range = budgets.GetValueOrDefault(id, new CharacterRange(1, 80));
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
