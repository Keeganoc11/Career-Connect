using System.Globalization;
using System.Text.RegularExpressions;
using CareerConnect.Api.Domain;

namespace CareerConnect.Api.Services;

public enum EntrySection
{
    Work,
    Projects,
}

/// <summary>
/// One job or project on the page: its heading lines (title, company, dates)
/// and the bullets under it. A slot tailoring can fill with a different entry
/// of the same kind, as long as the replacement takes exactly the same shape.
/// </summary>
public record ResumeEntry(string SlotId, EntrySection Section, List<string> HeaderLineIds, List<string> BulletLineIds);

public static partial class ResumeEntries
{
    /// <summary>
    /// Entries under work and project headings. An entry starts at a line whose
    /// first run is bold, takes the plain lines after it as more heading, and
    /// takes the bullets after those. Education and skills have no slots.
    /// </summary>
    public static List<ResumeEntry> Find(ResumeLayout layout)
    {
        var entries = new List<ResumeEntry>();
        EntrySection? section = null;
        List<string>? header = null;
        List<string>? bullets = null;

        void Close()
        {
            if (section is { } s && header is { Count: > 0 } && bullets is { Count: > 0 })
            {
                entries.Add(new ResumeEntry($"S{header[0][1..]}", s, header, bullets));
            }
            header = null;
            bullets = null;
        }

        foreach (var line in layout.Lines)
        {
            switch (line.Kind)
            {
                case ResumeLineKind.Heading:
                    Close();
                    section = SectionOf(line.Text);
                    break;

                case ResumeLineKind.Blank:
                    break;

                case ResumeLineKind.Bullet when header is not null:
                    bullets ??= [];
                    bullets.Add(line.Id);
                    break;

                case ResumeLineKind.Text when section is not null:
                    var startsBold = line.Runs.FirstOrDefault(r => r.Text.Trim().Length > 0)?.Bold == true;
                    if (startsBold || bullets is not null || header is null)
                    {
                        Close();
                        if (startsBold)
                        {
                            header = [line.Id];
                        }
                    }
                    else
                    {
                        header.Add(line.Id);
                    }
                    break;

                default:
                    Close();
                    break;
            }
        }

        Close();
        return entries;
    }

    /// <summary>The parts of a heading line that hold words — title, dates, company — in order.</summary>
    public static List<ResumeRun> Fields(ResumeLine line) =>
        line.Runs.Where(r => r.Text.Trim().Length > 0).ToList();

    private static EntrySection? SectionOf(string heading)
    {
        var upper = heading.ToUpperInvariant();
        if (upper.Contains("PROJECT"))
        {
            return EntrySection.Projects;
        }
        if (upper.Contains("EXPERIENCE") || upper.Contains("EMPLOYMENT") || upper.Contains("WORK"))
        {
            return EntrySection.Work;
        }
        return null;
    }

    /// <summary>
    /// A date range read out of heading text, as months since year zero —
    /// "June 2025 - August 2025", "Jan 2026 – Present". Null when there's no year.
    /// </summary>
    public static (int Start, int End)? DateRange(string text)
    {
        var points = DatePoint().Matches(text)
            .Select(m => m.Groups["present"].Success
                ? int.MaxValue
                : int.Parse(m.Groups["year"].Value, CultureInfo.InvariantCulture) * 12 + MonthOf(m.Groups["month"].Value))
            .ToList();

        return points.Count == 0 ? null : (points[0], points[^1]);
    }

    private static int MonthOf(string month)
    {
        if (month.Length < 3)
        {
            return 12; // A bare year sorts as the end of that year.
        }
        var index = Array.FindIndex(CultureInfo.InvariantCulture.DateTimeFormat.AbbreviatedMonthNames,
            m => m.Length > 0 && month.StartsWith(m, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? 12 : index + 1;
    }

    [GeneratedRegex(@"(?:(?<month>[A-Za-z]{3,9})\.?\s+)?(?<year>(?:19|20)\d{2})|(?<present>\b(?:Present|Current|Now)\b)", RegexOptions.IgnoreCase)]
    private static partial Regex DatePoint();
}
