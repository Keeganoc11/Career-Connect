using System.Text.RegularExpressions;
using CareerConnect.Api.Domain;

namespace CareerConnect.Api.Services;

public abstract record SwapOutcome
{
    /// <param name="BulletLineIds">The swapped bullets, which still go through the fit check.</param>
    public sealed record Applied(ResumeLayout Layout, string Label, List<string> LineIds, List<string> BulletLineIds) : SwapOutcome;
    public sealed record Rejected(string Why) : SwapOutcome;
}

/// <summary>
/// Checks and applies one entry swap. Every rule a model could talk its way
/// around is enforced here: the slot's exact shape, heading words that come
/// from the extra facts, dates that exist, work history that stays in order,
/// and headings that still fit their lines.
/// </summary>
public partial class EntrySwapper(IResumeRenderer renderer)
{
    /// <summary>A run ending this close to the margin was right-aligned, and stays that way.</summary>
    private const double RightAlignSlack = 8;

    public SwapOutcome Apply(
        ResumeLayout current, EntrySwapProposal proposal, string? extraFacts, Func<string, string?> checkText)
    {
        if (string.IsNullOrWhiteSpace(extraFacts))
        {
            return new SwapOutcome.Rejected("There are no extra facts to swap an entry in from.");
        }

        var entries = ResumeEntries.Find(current);
        var slot = entries.FirstOrDefault(e => e.SlotId == proposal.SlotId);
        if (slot is null)
        {
            return new SwapOutcome.Rejected("Not an entry on the page.");
        }

        if (!proposal.HeaderLines.Select(h => h.LineId).SequenceEqual(slot.HeaderLineIds)
            || !proposal.Bullets.Select(b => b.LineId).SequenceEqual(slot.BulletLineIds))
        {
            return new SwapOutcome.Rejected("Didn't fill the entry's heading lines and bullets exactly.");
        }

        var oldHeading = string.Join(" ", slot.HeaderLineIds.Select(id => current.Find(id)!.Text));
        var newHeading = string.Join(" ", proposal.HeaderLines.SelectMany(h => h.Fields));

        foreach (var header in proposal.HeaderLines)
        {
            var line = current.Find(header.LineId)!;
            if (line.Continuations.Count > 0 || header.Fields.Count != ResumeEntries.Fields(line).Count || header.Fields.Any(f => f.Trim().Length == 0))
            {
                return new SwapOutcome.Rejected("The heading didn't keep the same fields (title, company, dates).");
            }
        }

        if (Normalize(oldHeading) == Normalize(newHeading))
        {
            return new SwapOutcome.Rejected("That's the entry already there.");
        }

        if (ResumeEntries.DateRange(newHeading) is null)
        {
            return new SwapOutcome.Rejected("Your extra facts don't give dates for that entry, so it can't be swapped in.");
        }

        // Heading words — names, titles, companies, places — must come from the
        // extra facts. Dates are checked separately; month spellings vary.
        var factWords = Words(extraFacts);
        var unsupported = Words(newHeading).Where(w => !factWords.Contains(w)).ToList();
        if (unsupported.Count > 0)
        {
            return new SwapOutcome.Rejected($"The heading uses \"{string.Join(", ", unsupported)}\", which isn't in your extra facts.");
        }

        var factYears = Year().Matches(extraFacts).Select(m => m.Value).ToHashSet();
        if (Year().Matches(newHeading).Any(m => !factYears.Contains(m.Value)))
        {
            return new SwapOutcome.Rejected("The dates aren't the ones in your extra facts.");
        }

        var newTitle = Normalize(proposal.HeaderLines[0].Fields[0]);
        if (entries.Where(e => e.SlotId != slot.SlotId)
            .Any(e => Normalize(ResumeEntries.Fields(current.Find(e.HeaderLineIds[0])!).FirstOrDefault()?.Text ?? "") == newTitle))
        {
            return new SwapOutcome.Rejected("That entry is already elsewhere on the page.");
        }

        foreach (var bullet in proposal.Bullets)
        {
            if (checkText(bullet.Text) is { } problem)
            {
                return new SwapOutcome.Rejected($"A bullet was rejected: {problem}");
            }
        }

        var layout = current;
        foreach (var header in proposal.HeaderLines)
        {
            var rebuilt = RebuildHeading(current, current.Find(header.LineId)!, header.Fields);
            if (rebuilt is null)
            {
                return new SwapOutcome.Rejected("The new heading doesn't fit on its line.");
            }
            layout = layout.WithLine(rebuilt);
        }

        if (slot.Section == EntrySection.Work && !InDateOrder(layout))
        {
            return new SwapOutcome.Rejected("That job's dates would put your work history out of order.");
        }

        var style = BulletTextStyle(current, slot);
        foreach (var bullet in proposal.Bullets)
        {
            layout = layout.WithLine(RebuildBullet(current.Find(bullet.LineId)!, bullet.Text.Trim(), style));
        }

        var oldTitle = ResumeEntries.Fields(current.Find(slot.HeaderLineIds[0])!).First().Text.Trim();
        return new SwapOutcome.Applied(
            layout,
            $"{oldTitle} → {proposal.HeaderLines[0].Fields[0].Trim()}",
            [.. slot.HeaderLineIds, .. slot.BulletLineIds],
            slot.BulletLineIds);
    }

    /// <summary>
    /// New words in each field, positioned the way the old ones were:
    /// right-aligned dates keep their right edge, a field that followed the one
    /// before it keeps following it, and anything on a tab stop stays put.
    /// </summary>
    private ResumeLine? RebuildHeading(ResumeLayout layout, ResumeLine line, List<string> fields)
    {
        var runs = new List<ResumeRun>();
        var field = 0;
        double previousEnd = 0, previousEndWithSpace = 0, originalPreviousEndWithSpace = 0;

        foreach (var run in line.Runs)
        {
            if (run.Text.Trim().Length == 0)
            {
                continue;
            }

            var trailingSpace = run.Text.EndsWith(' ');
            var text = fields[field].Trim() + (trailingSpace ? " " : "");
            var space = renderer.SpaceWidth(layout, run);
            var originalEnd = run.X + renderer.TextWidth(layout, run);
            var width = renderer.TextWidth(layout, run with { Text = text });

            var x = field == 0
                ? run.X
                : originalEnd >= layout.RightLimit - RightAlignSlack
                    ? originalEnd - width
                    : run.X - originalPreviousEndWithSpace <= 3
                        ? previousEndWithSpace
                        : run.X;

            if ((field > 0 && x < previousEnd + 1) || x + width > layout.RightLimit + 0.5)
            {
                return null;
            }

            runs.Add(run with { Text = text, X = Math.Round(x, 2) });
            previousEnd = x + width;
            previousEndWithSpace = previousEnd + (trailingSpace ? space : 0);
            originalPreviousEndWithSpace = originalEnd + (trailingSpace ? space : 0);
            field++;
        }

        return new ResumeLine { Id = line.Id, Kind = line.Kind, Baseline = line.Baseline, Runs = runs, Edited = true };
    }

    /// <summary>
    /// The look of a normal bullet in this entry. A link bullet (blue, underlined)
    /// becomes an ordinary bullet when its slot takes an entry with no link.
    /// </summary>
    private static ResumeRun BulletTextStyle(ResumeLayout layout, ResumeEntry slot)
    {
        var bullets = slot.BulletLineIds.Select(id => layout.Find(id)!).ToList();
        var normal = bullets.FirstOrDefault(b => b.EditableFrom is not null) ?? bullets[0];
        return normal.Runs[normal.EditableFrom ?? Math.Min(1, normal.Runs.Count - 1)] with { Color = null };
    }

    private static ResumeLine RebuildBullet(ResumeLine line, string text, ResumeRun style)
    {
        var from = line.EditableFrom ?? Math.Min(1, line.Runs.Count - 1);
        var editable = new ResumeLine
        {
            Id = line.Id,
            Kind = line.Kind,
            Baseline = line.Baseline,
            Runs = [.. line.Runs.Take(from), style with { Text = line.Runs[from].Text, X = line.Runs[from].X }],
            Continuations = line.Continuations,
            EditableFrom = from,
        };
        return editable.WithEditableText(text);
    }

    /// <summary>Jobs newest first: each one ends no later than the one above it.</summary>
    private static bool InDateOrder(ResumeLayout layout)
    {
        var ends = ResumeEntries.Find(layout)
            .Where(e => e.Section == EntrySection.Work)
            .Select(e => ResumeEntries.DateRange(string.Join(" ", e.HeaderLineIds.Select(id => layout.Find(id)!.Text)))?.End)
            .OfType<int>()
            .ToList();

        return ends.Zip(ends.Skip(1)).All(pair => pair.First >= pair.Second);
    }

    private static readonly HashSet<string> DateWords =
    [
        "jan", "january", "feb", "february", "mar", "march", "apr", "april", "may", "jun", "june", "jul", "july",
        "aug", "august", "sep", "sept", "september", "oct", "october", "nov", "november", "dec", "december",
        "present", "current", "now",
    ];

    private static HashSet<string> Words(string text) =>
        WordPattern().Matches(text.ToLowerInvariant())
            .Select(m => m.Value)
            .Where(w => w.Length > 1 && !DateWords.Contains(w) && !Year().IsMatch(w))
            .ToHashSet();

    private static string Normalize(string text) => string.Join(' ', WordPattern().Matches(text.ToLowerInvariant()).Select(m => m.Value));

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex WordPattern();

    [GeneratedRegex(@"\b(?:19|20)\d{2}\b")]
    private static partial Regex Year();
}
