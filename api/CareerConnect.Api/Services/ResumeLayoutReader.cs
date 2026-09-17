using System.Text;
using CareerConnect.Api.Domain;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Actions;
using UglyToad.PdfPig.Content;

namespace CareerConnect.Api.Services;

public abstract record ResumeLayoutReadOutcome
{
    public sealed record Success(ResumeLayout Layout) : ResumeLayoutReadOutcome;
    public sealed record Failed(string Message) : ResumeLayoutReadOutcome;
}

public interface IResumeLayoutReader
{
    ResumeLayoutReadOutcome Read(byte[] pdf);
}

/// <summary>
/// Reads an uploaded one-page resume PDF into positioned lines. Deterministic
/// on purpose: a model would paraphrase or "fix" what it read, and the whole
/// point is that the base resume is taken exactly as written.
/// </summary>
public class ResumeLayoutReader : IResumeLayoutReader
{
    private static readonly HashSet<string> BulletGlyphs = ["●", "•", "▪", "◦", "■", "‣"];

    /// <summary>Letters within this distance of a baseline belong to the same line.</summary>
    private const double BaselineTolerance = 1.0;

    public ResumeLayoutReadOutcome Read(byte[] pdf)
    {
        PdfDocument document;
        try
        {
            document = PdfDocument.Open(pdf);
        }
        catch (Exception)
        {
            return new ResumeLayoutReadOutcome.Failed("Couldn't open this PDF — it may be corrupted or password-protected.");
        }

        using (document)
        {
            if (document.NumberOfPages != 1)
            {
                return new ResumeLayoutReadOutcome.Failed(
                    $"This PDF has {document.NumberOfPages} pages. Tailored resumes are always one page, so upload a one-page version.");
            }

            var page = document.GetPage(1);
            var letters = page.Letters.Where(l => l.Value.Length > 0).ToList();

            if (letters.Count(l => !string.IsNullOrWhiteSpace(l.Value)) < 50)
            {
                return new ResumeLayoutReadOutcome.Failed(
                    "Couldn't find enough text in this PDF. If it's a scan or an image, export it from the original document instead.");
            }

            var unsupported = letters
                .Where(l => !string.IsNullOrWhiteSpace(l.Value) && FamilyOf(l) is null)
                .Select(l => CleanFontName(l.FontName))
                .Distinct()
                .ToList();

            if (unsupported.Count > 0)
            {
                return new ResumeLayoutReadOutcome.Failed(
                    $"This resume uses {string.Join(", ", unsupported)}. Keeping the format exact currently works for " +
                    "Times New Roman and Arial resumes — the fonts Google Docs and Word use by default.");
            }

            var lines = GroupIntoLines(letters);
            var layoutLines = new List<ResumeLine>();
            string? section = null;

            for (var i = 0; i < lines.Count; i++)
            {
                var line = BuildLine($"L{i + 1:00}", lines[i], section);
                if (line.Kind == ResumeLineKind.Heading)
                {
                    section = line.Text;
                }

                // A bullet or skill list that wrapped: fold the row into the
                // paragraph above it, so its words are rewritten as one piece.
                if (layoutLines.Count > 0 && IsContinuation(layoutLines[^1], line))
                {
                    var paragraph = layoutLines[^1];
                    layoutLines[^1] = new ResumeLine
                    {
                        Id = paragraph.Id,
                        Kind = paragraph.Kind,
                        Baseline = paragraph.Baseline,
                        Runs = paragraph.Runs,
                        EditableFrom = paragraph.EditableFrom,
                        Continuations = [.. paragraph.Continuations, new ResumeRow(line.Baseline, line.Runs)],
                    };
                    continue;
                }

                layoutLines.Add(line);
            }

            var leftMargin = layoutLines
                .Where(l => l.Runs.Count > 0)
                .Min(l => l.Runs[0].X);

            var links = page.GetAnnotations()
                .Where(a => a.Action is UriAction)
                .Select(a => new ResumeLink(
                    ((UriAction)a.Action!).Uri,
                    a.Rectangle.Left,
                    a.Rectangle.Bottom,
                    a.Rectangle.Width,
                    a.Rectangle.Height))
                .ToList();

            return new ResumeLayoutReadOutcome.Success(new ResumeLayout
            {
                PageWidth = page.Width,
                PageHeight = page.Height,
                RightLimit = page.Width - leftMargin,
                Lines = layoutLines,
                Links = links,
                Rules = ReadRules(page),
            });
        }
    }

    private static List<(double Baseline, List<Letter> Letters)> GroupIntoLines(List<Letter> letters)
    {
        var lines = new List<(double Baseline, List<Letter> Letters)>();

        foreach (var letter in letters.OrderByDescending(l => l.StartBaseLine.Y).ThenBy(l => l.StartBaseLine.X))
        {
            var y = letter.StartBaseLine.Y;
            if (lines.Count > 0 && Math.Abs(lines[^1].Baseline - y) <= BaselineTolerance)
            {
                lines[^1].Letters.Add(letter);
            }
            else
            {
                lines.Add((y, [letter]));
            }
        }

        return lines
            .Select(l => (l.Baseline, l.Letters.OrderBy(x => x.StartBaseLine.X).ToList()))
            .ToList();
    }

    private static ResumeLine BuildLine(string id, (double Baseline, List<Letter> Letters) source, string? section)
    {
        var letters = source.Letters;
        var baseline = Math.Round(source.Baseline, 2);

        if (letters.All(l => string.IsNullOrWhiteSpace(l.Value)))
        {
            return new ResumeLine { Id = id, Kind = ResumeLineKind.Blank, Baseline = baseline, Runs = [] };
        }

        var firstVisible = letters.First(l => !string.IsNullOrWhiteSpace(l.Value));
        var isBullet = BulletGlyphs.Contains(firstVisible.Value);

        var runs = BuildRuns(letters, breakAfterLeadingBullet: isBullet);
        var text = string.Concat(runs.Select(r => r.Text)).Trim();

        if (isBullet)
        {
            var body = string.Concat(runs.Skip(1).Select(r => r.Text)).Trim();
            return new ResumeLine
            {
                Id = id,
                Kind = ResumeLineKind.Bullet,
                Baseline = baseline,
                Runs = runs,
                // A bare link ("https://apps.apple.com/…") is an address, not a
                // claim to reword.
                EditableFrom = runs.Count > 1 && !LooksLikeLink(body) ? 1 : null,
            };
        }

        var allBold = runs.All(r => r.Bold || string.IsNullOrWhiteSpace(r.Text));
        if (allBold && text.Any(char.IsLetter) && text == text.ToUpperInvariant() && text.Length <= 60)
        {
            return new ResumeLine { Id = id, Kind = ResumeLineKind.Heading, Baseline = baseline, Runs = runs };
        }

        var inSkills = section?.Contains("SKILL", StringComparison.OrdinalIgnoreCase) == true;
        if (inSkills && runs.Count >= 2 && runs[0].Bold && runs[0].Text.TrimEnd().EndsWith(':'))
        {
            return new ResumeLine
            {
                Id = id,
                Kind = ResumeLineKind.Skill,
                Baseline = baseline,
                Runs = runs,
                EditableFrom = 1,
            };
        }

        return new ResumeLine { Id = id, Kind = ResumeLineKind.Text, Baseline = baseline, Runs = runs };
    }

    /// <summary>
    /// Whether a row is the wrapped remainder of the paragraph above: plain
    /// text directly below it, starting where that paragraph's words start —
    /// a bullet's text indent, or a skill line's left edge — and never a new
    /// bullet, heading, or bold label.
    /// </summary>
    private static bool IsContinuation(ResumeLine paragraph, ResumeLine row)
    {
        if (paragraph.Kind is not (ResumeLineKind.Bullet or ResumeLineKind.Skill)
            || row.Kind != ResumeLineKind.Text
            || row.Runs.Count == 0
            || paragraph.Runs.Count < 2
            || row.Runs[0].Bold)
        {
            return false;
        }

        var previousBaseline = paragraph.Continuations.Count > 0 ? paragraph.Continuations[^1].Baseline : paragraph.Baseline;
        var gap = previousBaseline - row.Baseline;
        var size = row.Runs[0].FontSize;
        if (gap <= 0 || gap > size * 1.6)
        {
            return false;
        }

        var indent = paragraph.Kind == ResumeLineKind.Bullet ? paragraph.Runs[1].X : paragraph.Runs[0].X;
        return Math.Abs(row.Runs[0].X - indent) <= 1.5;
    }

    /// <summary>
    /// Splits a line wherever the font changes. Whitespace never starts a run —
    /// it trails the run before it — so every run begins at a real character
    /// and replacing a run's words can't shift where they start.
    /// </summary>
    private static List<ResumeRun> BuildRuns(List<Letter> letters, bool breakAfterLeadingBullet)
    {
        var runs = new List<ResumeRun>();
        var text = new StringBuilder();
        Letter? runStart = null;
        Letter? previous = null;
        // Set right after a leading bullet glyph: the words start their own run
        // even when they share the glyph's font.
        var forceBreak = false;

        void Flush()
        {
            if (runStart is not null && text.Length > 0)
            {
                var (family, bold, italic) = StyleOf(runStart);
                runs.Add(new ResumeRun(text.ToString(), family, bold, italic,
                    Math.Round(runStart.PointSize, 2), Math.Round(runStart.StartBaseLine.X, 2), ColorOf(runStart)));
            }
            text.Clear();
            runStart = null;
        }

        foreach (var letter in letters)
        {
            if (string.IsNullOrWhiteSpace(letter.Value))
            {
                if (runStart is not null)
                {
                    text.Append(' ');
                }
                previous = letter;
                continue;
            }

            var newRun = runStart is null
                || forceBreak
                || StyleOf(letter) != StyleOf(runStart)
                || ColorOf(letter) != ColorOf(runStart)
                || Math.Abs(letter.PointSize - runStart.PointSize) > 0.1;
            forceBreak = false;

            if (newRun)
            {
                Flush();
                runStart = letter;
            }
            else if (previous is not null && !string.IsNullOrWhiteSpace(previous.Value)
                     && letter.StartBaseLine.X - previous.EndBaseLine.X > letter.PointSize * 0.15)
            {
                // Some PDFs position words instead of emitting a space glyph.
                text.Append(' ');
            }

            text.Append(letter.Value);

            if (breakAfterLeadingBullet && runs.Count == 0 && text.Length == letter.Value.Length
                && BulletGlyphs.Contains(letter.Value))
            {
                forceBreak = true;
            }

            previous = letter;
        }

        Flush();

        // Whitespace-only trailing runs carry nothing and would only skew measurements.
        while (runs.Count > 0 && string.IsNullOrWhiteSpace(runs[^1].Text))
        {
            runs.RemoveAt(runs.Count - 1);
        }

        return runs;
    }

    /// <summary>
    /// Visible straight lines: heading rules and link underlines. Word and
    /// Google Docs exports also paint white rectangles behind every paragraph;
    /// those are skipped, as is anything that isn't a thin straight line.
    /// </summary>
    private static List<ResumeRule> ReadRules(Page page)
    {
        var rules = new List<ResumeRule>();

        foreach (var path in page.ExperimentalAccess.Paths)
        {
            if (path.IsStroked && ToColor(path.StrokeColor) is { } stroke && !IsWhite(stroke))
            {
                foreach (var subpath in path)
                {
                    UglyToad.PdfPig.Core.PdfPoint? previous = null;
                    foreach (var command in subpath.Commands)
                    {
                        switch (command)
                        {
                            case UglyToad.PdfPig.Core.PdfSubpath.Move move:
                                previous = move.Location;
                                break;
                            case UglyToad.PdfPig.Core.PdfSubpath.Line line when previous is { } from:
                                if (Math.Abs(line.From.Y - line.To.Y) < 0.1 || Math.Abs(line.From.X - line.To.X) < 0.1)
                                {
                                    rules.Add(new ResumeRule(line.From.X, line.From.Y, line.To.X, line.To.Y,
                                        Math.Max(path.LineWidth, 0.25), stroke));
                                }
                                previous = line.To;
                                break;
                            default:
                                previous = null;
                                break;
                        }
                    }
                }
            }

            if (path.IsFilled && ToColor(path.FillColor) is { } fill && !IsWhite(fill))
            {
                // A rule drawn as a hairline-thin filled box rather than a stroke.
                var box = path.GetBoundingRectangle();
                if (box is { } rect && rect.Height <= 3 && rect.Width >= 10)
                {
                    var y = rect.Bottom + rect.Height / 2;
                    rules.Add(new ResumeRule(rect.Left, y, rect.Right, y, Math.Max(rect.Height, 0.25), fill));
                }
            }
        }

        return rules;
    }

    private static ResumeColor? ColorOf(Letter letter)
    {
        var color = ToColor(letter.Color);
        return color is null || color.IsBlack ? null : color;
    }

    private static ResumeColor? ToColor(UglyToad.PdfPig.Graphics.Colors.IColor? color)
    {
        if (color is null)
        {
            return null;
        }

        var (r, g, b) = color.ToRGBValues();
        return new ResumeColor(Math.Round(r, 4), Math.Round(g, 4), Math.Round(b, 4));
    }

    private static bool IsWhite(ResumeColor color) => color.R > 0.98 && color.G > 0.98 && color.B > 0.98;

    private static bool LooksLikeLink(string text) =>
        !text.Contains(' ') && (text.Contains("://") || text.StartsWith("www.", StringComparison.OrdinalIgnoreCase));

    private static (ResumeFontFamily Family, bool Bold, bool Italic) StyleOf(Letter letter)
    {
        var name = CleanFontName(letter.FontName);
        var bold = letter.Font.IsBold || name.Contains("Bold", StringComparison.OrdinalIgnoreCase);
        var italic = letter.Font.IsItalic
            || name.Contains("Italic", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Oblique", StringComparison.OrdinalIgnoreCase);
        return (FamilyOf(letter) ?? ResumeFontFamily.Serif, bold, italic);
    }

    private static ResumeFontFamily? FamilyOf(Letter letter)
    {
        var name = CleanFontName(letter.FontName).Replace(" ", "").Replace("-", "");

        if (name.Contains("TimesNewRoman", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Times", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Tinos", StringComparison.OrdinalIgnoreCase)
            || name.Contains("LiberationSerif", StringComparison.OrdinalIgnoreCase))
        {
            return ResumeFontFamily.Serif;
        }

        if (name.Contains("Arial", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Helvetica", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Arimo", StringComparison.OrdinalIgnoreCase)
            || name.Contains("LiberationSans", StringComparison.OrdinalIgnoreCase))
        {
            return ResumeFontFamily.Sans;
        }

        return null;
    }

    /// <summary>"BAAAAA+TimesNewRomanPSMT" → "TimesNewRomanPSMT": the prefix only marks a font subset.</summary>
    private static string CleanFontName(string? fontName) =>
        (fontName ?? "unknown font").Split('+').Last();
}
