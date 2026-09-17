using CareerConnect.Api.Domain;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;

namespace CareerConnect.Api.Services;

/// <summary>
/// How far one paragraph reaches and how many rows its words need, measured
/// with the fonts it will actually be drawn in.
/// </summary>
public record LineMeasurement(string LineId, double EndX, double Limit, int RowsNeeded = 1, int RowsAvailable = 1)
{
    public bool Fits => EndX <= Limit + FitTolerance && RowsNeeded <= RowsAvailable;

    /// <summary>Fits, but leaves one of its rows empty — a visible hole in a paragraph that used to fill it.</summary>
    public bool LeavesGap => RowsNeeded < RowsAvailable;

    public double Overflow => Math.Max(0, EndX - Limit);

    /// <summary>A hair of slack for float noise — well under the width of one character.</summary>
    private const double FitTolerance = 0.25;
}

public interface IResumeRenderer
{
    /// <summary>A one-page PDF of the layout, every line at its original position.</summary>
    byte[] Render(ResumeLayout layout);

    /// <summary>Measures every editable line. Locked lines never change, so they never need checking.</summary>
    IReadOnlyList<LineMeasurement> MeasureEditableLines(ResumeLayout layout);

    /// <summary>Measures one line. Throws <see cref="ResumeRenderException"/> if the fonts can't draw it.</summary>
    LineMeasurement MeasureLine(ResumeLayout layout, ResumeLine line);

    /// <summary>
    /// Roughly how many characters fill this paragraph's rows — a budget to hand
    /// a model, which can't measure points itself. A one-row line only has a
    /// maximum; a wrapped one also has a minimum, below which a row sits empty.
    /// The real check is always <see cref="MeasureLine"/>.
    /// </summary>
    CharacterRange CharacterBudget(ResumeLayout layout, ResumeLine line);

    /// <summary>The drawn width of a run's text, without trailing spaces.</summary>
    double TextWidth(ResumeLayout layout, ResumeRun run);

    /// <summary>The width of one space in a run's font.</summary>
    double SpaceWidth(ResumeLayout layout, ResumeRun run);
}

public record CharacterRange(int Min, int Max);

/// <summary>Thrown when a line uses a character the embedded fonts can't draw.</summary>
public class ResumeRenderException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Draws a <see cref="ResumeLayout"/> with PdfPig's writer and the embedded
/// Tinos/Arimo fonts — metric-compatible with Times New Roman and Arial, so a
/// line measures the same width it would in the original document.
/// </summary>
public class ResumeRenderer : IResumeRenderer
{
    public byte[] Render(ResumeLayout layout)
    {
        using var session = new Session(layout.PageWidth, layout.PageHeight);

        var title = layout.Lines.FirstOrDefault(l => l.Kind != ResumeLineKind.Blank)?.Text;
        if (!string.IsNullOrWhiteSpace(title))
        {
            session.Builder.DocumentInformation.Title = $"{title} — Resume";
        }

        foreach (var line in layout.Lines)
        {
            session.DrawLine(line, layout.RightLimit);
        }

        foreach (var rule in layout.Rules.Where(rule => !UnderEditedWords(layout, rule)))
        {
            session.DrawRule(rule);
        }

        foreach (var link in layout.Links)
        {
            session.Page.AddLink(link.Uri, new PdfRectangle(link.X, link.Y, link.X + link.Width, link.Y + link.Height));
        }

        return session.Builder.Build();
    }

    /// <summary>
    /// A thin horizontal line just below an edited line's words is an
    /// underline for words that are gone. Heading rules sit under locked
    /// headings, so they're never affected.
    /// </summary>
    private static bool UnderEditedWords(ResumeLayout layout, ResumeRule rule)
    {
        if (Math.Abs(rule.Y1 - rule.Y2) > 0.1)
        {
            return false;
        }

        return layout.Lines
            .Where(l => l.Edited && l.Runs.Count > 0)
            .Any(l => new[] { (l.Baseline, X: l.Runs[l.EditableFrom ?? 0].X) }
                .Concat(l.Continuations.Select(c => (c.Baseline, X: c.Runs.FirstOrDefault()?.X ?? 0)))
                .Any(row => rule.Y1 <= row.Baseline + 0.5 && rule.Y1 >= row.Baseline - 4 && Math.Max(rule.X1, rule.X2) > row.X));
    }

    public IReadOnlyList<LineMeasurement> MeasureEditableLines(ResumeLayout layout)
    {
        using var session = new Session(layout.PageWidth, layout.PageHeight);

        return layout.Lines
            .Where(l => l.Editable)
            .Select(l => session.Measure(l, layout.RightLimit))
            .ToList();
    }

    public LineMeasurement MeasureLine(ResumeLayout layout, ResumeLine line)
    {
        using var session = new Session(layout.PageWidth, layout.PageHeight);
        return session.Measure(line, layout.RightLimit);
    }

    public double TextWidth(ResumeLayout layout, ResumeRun run)
    {
        using var session = new Session(layout.PageWidth, layout.PageHeight);
        return session.EndOf(run with { X = 0 });
    }

    public double SpaceWidth(ResumeLayout layout, ResumeRun run)
    {
        using var session = new Session(layout.PageWidth, layout.PageHeight);
        return session.EndOf(run with { Text = "x x", X = 0 }) - session.EndOf(run with { Text = "xx", X = 0 });
    }

    public CharacterRange CharacterBudget(ResumeLayout layout, ResumeLine line)
    {
        // A locked bullet (a link line) is measured like any bullet: an entry
        // swap can put words there.
        if (line.EditableFrom is null && line.Kind == ResumeLineKind.Bullet && line.Runs.Count > 1)
        {
            line = new ResumeLine
            {
                Id = line.Id,
                Kind = line.Kind,
                Baseline = line.Baseline,
                Runs = line.Runs,
                Continuations = line.Continuations,
                EditableFrom = 1,
            };
        }

        if (line.EditableFrom is not { } from || line.EditableText is not { Length: > 0 } text)
        {
            return new CharacterRange(0, 0);
        }

        using var session = new Session(layout.PageWidth, layout.PageHeight);

        var style = session.Place(line.Runs)[from];
        var width = session.EndOf(style with { Text = text, X = 0 });
        if (width <= 0)
        {
            return new CharacterRange(1, text.Length);
        }

        var perCharacter = width / text.Length;
        var rowWidths = Enumerable.Range(0, line.RowCount)
            .Select(row => layout.RightLimit - Session.RowStart(line, style, row))
            .ToList();

        // Word wrap never uses a row's full width, so the estimate is trimmed
        // a little per break; the real check measures anyway.
        var max = (int)Math.Floor(rowWidths.Sum() / perCharacter) - 3 * (line.RowCount - 1);
        var min = line.RowCount == 1
            ? 1
            : (int)Math.Floor((rowWidths.Sum() - rowWidths[^1]) / perCharacter) + 8;
        return new CharacterRange(Math.Min(min, max), max);
    }

    /// <summary>
    /// One PdfPig builder with the fonts it has loaded. Measuring needs a page
    /// to measure against, so measurement and drawing share this.
    /// </summary>
    private sealed class Session : IDisposable
    {
        private readonly Dictionary<(ResumeFontFamily, bool, bool), PdfDocumentBuilder.AddedFont> _fonts = [];

        public PdfDocumentBuilder Builder { get; } = new();
        public PdfPageBuilder Page { get; }

        public Session(double width, double height)
        {
            Page = Builder.AddPage(width, height);
        }

        public void Draw(ResumeRun run, double baseline, bool isLastOnLine)
        {
            // A space between runs ("Frontend: " then the skills) is drawn, not
            // dropped: text extractors — including applicant tracking systems —
            // read it as the word break.
            var text = isLastOnLine ? run.Text.TrimEnd() : run.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            try
            {
                if (run.Color is { IsBlack: false } color)
                {
                    Page.SetTextAndFillColor(Channel(color.R), Channel(color.G), Channel(color.B));
                    Page.AddText(text, run.FontSize, new PdfPoint(run.X, baseline), Font(run));
                    Page.ResetColor();
                }
                else
                {
                    Page.AddText(text, run.FontSize, new PdfPoint(run.X, baseline), Font(run));
                }
            }
            catch (Exception ex) when (ex is not ResumeRenderException)
            {
                throw new ResumeRenderException($"Couldn't draw \"{text}\" — it may contain a character the resume font doesn't have.", ex);
            }
        }

        private const double FitTolerance = 0.25;

        public void DrawLine(ResumeLine line, double limit)
        {
            var placed = Place(line.Runs);

            if (line.Replacement is null || line.EditableFrom is not { } from)
            {
                DrawRow(placed, line.Baseline);
                foreach (var row in line.Continuations)
                {
                    DrawRow(Place(row.Runs), row.Baseline);
                }
                return;
            }

            for (var i = 0; i < from; i++)
            {
                Draw(placed[i], line.Baseline, isLastOnLine: false);
            }

            var style = placed[from];
            var rows = Wrap(line, style, limit);
            for (var row = 0; row < Math.Min(rows.Count, line.RowCount); row++)
            {
                Draw(style with { Text = rows[row], X = RowStart(line, style, row) }, RowBaseline(line, row), isLastOnLine: true);
            }
        }

        public LineMeasurement Measure(ResumeLine line, double limit)
        {
            var placed = Place(line.Runs);

            if (line.Replacement is null || line.EditableFrom is not { } from)
            {
                var ends = new[] { placed.Count == 0 ? 0 : EndOf(placed[^1]) }
                    .Concat(line.Continuations.Select(c => Place(c.Runs) is { Count: > 0 } runs ? EndOf(runs[^1]) : 0));
                return new LineMeasurement(line.Id, ends.Max(), limit, line.RowCount, line.RowCount);
            }

            var style = placed[from];
            var rows = Wrap(line, style, limit);
            var end = rows.Count == 0
                ? style.X
                : rows.Select((text, row) => EndOf(style with { Text = text, X = RowStart(line, style, row) })).Max();
            return new LineMeasurement(line.Id, end, limit, Math.Max(rows.Count, 1), line.RowCount);
        }

        /// <summary>
        /// Breaks replacement words across rows the way a word processor would:
        /// as many words per row as fit before the margin. Returns every row the
        /// words need, even beyond the ones available, so the caller can tell.
        /// </summary>
        private List<string> Wrap(ResumeLine line, ResumeRun style, double limit)
        {
            var words = (line.Replacement ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var rows = new List<string>();
            var current = "";

            foreach (var word in words)
            {
                var candidate = current.Length == 0 ? word : $"{current} {word}";
                if (current.Length == 0
                    || EndOf(style with { Text = candidate, X = RowStart(line, style, rows.Count) }) <= limit + FitTolerance)
                {
                    current = candidate;
                }
                else
                {
                    rows.Add(current);
                    current = word;
                }
            }

            if (current.Length > 0)
            {
                rows.Add(current);
            }

            return rows;
        }

        /// <summary>Where a row's words start: the first row after its fixed prefix, later rows at their own indent.</summary>
        public static double RowStart(ResumeLine line, ResumeRun firstRowStyle, int row) =>
            row == 0 || line.Continuations.Count == 0
                ? firstRowStyle.X
                : line.Continuations[Math.Min(row, line.Continuations.Count) - 1].Runs.FirstOrDefault()?.X ?? firstRowStyle.X;

        private static double RowBaseline(ResumeLine line, int row) =>
            row == 0 ? line.Baseline : line.Continuations[row - 1].Baseline;

        private void DrawRow(List<ResumeRun> runs, double baseline)
        {
            for (var i = 0; i < runs.Count; i++)
            {
                Draw(runs[i], baseline, isLastOnLine: i == runs.Count - 1);
            }
        }

        /// <summary>
        /// Each run at its original x, unless the run before it (with its
        /// trailing space) now reaches past that point. Tinos Bold runs a
        /// fraction wider than some Times renderings, and without this a date
        /// after a bold title can touch the title's last word.
        /// </summary>
        public List<ResumeRun> Place(List<ResumeRun> runs)
        {
            var placed = new List<ResumeRun>(runs.Count);
            foreach (var run in runs)
            {
                if (placed.Count > 0)
                {
                    var previous = placed[^1];
                    var reach = SpaceEndOf(previous);
                    if (reach > run.X)
                    {
                        placed.Add(run with { X = reach });
                        continue;
                    }
                }
                placed.Add(run);
            }
            return placed;
        }

        /// <summary>Where a run ends including any trailing space, which separates it from the next run.</summary>
        private double SpaceEndOf(ResumeRun run)
        {
            if (!run.Text.EndsWith(' '))
            {
                return EndOf(run);
            }

            var trimmed = EndOf(run);
            var spaceWidth = EndOf(run with { Text = "x x", X = 0 }) - EndOf(run with { Text = "xx", X = 0 });
            return trimmed + spaceWidth;
        }

        public void DrawRule(ResumeRule rule)
        {
            Page.SetStrokeColor(Channel(rule.Color.R), Channel(rule.Color.G), Channel(rule.Color.B));
            Page.DrawLine(new PdfPoint(rule.X1, rule.Y1), new PdfPoint(rule.X2, rule.Y2), rule.Width);
            Page.ResetColor();
        }

        private static byte Channel(double value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);

        public double EndOf(ResumeRun run)
        {
            var text = run.Text.TrimEnd();
            if (text.Length == 0)
            {
                return run.X;
            }

            try
            {
                var letters = Page.MeasureText(text, run.FontSize, new PdfPoint(run.X, 0), Font(run));
                return letters.Count == 0 ? run.X : letters.Max(l => l.EndBaseLine.X);
            }
            catch (Exception ex)
            {
                throw new ResumeRenderException($"Couldn't measure \"{text}\" — it may contain a character the resume font doesn't have.", ex);
            }
        }

        private PdfDocumentBuilder.AddedFont Font(ResumeRun run)
        {
            var key = (run.Family, run.Bold, run.Italic);
            if (!_fonts.TryGetValue(key, out var font))
            {
                font = Builder.AddTrueTypeFont(ResumeFonts.Get(run.Family, run.Bold, run.Italic));
                _fonts[key] = font;
            }

            return font;
        }

        public void Dispose() => Builder.Dispose();
    }
}

/// <summary>The embedded font files, loaded once. See Resources/Fonts for their licenses (SIL OFL 1.1).</summary>
public static class ResumeFonts
{
    private static readonly Dictionary<string, byte[]> Cache = [];
    private static readonly object CacheLock = new();

    public static byte[] Get(ResumeFontFamily family, bool bold, bool italic)
    {
        var style = (bold, italic) switch
        {
            (true, true) => "BoldItalic",
            (true, false) => "Bold",
            (false, true) => "Italic",
            _ => "Regular",
        };
        var name = $"{(family == ResumeFontFamily.Serif ? "Tinos" : "Arimo")}-{style}.ttf";

        lock (CacheLock)
        {
            if (Cache.TryGetValue(name, out var cached))
            {
                return cached;
            }

            var assembly = typeof(ResumeFonts).Assembly;
            var resource = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(name, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"Embedded font {name} is missing.");

            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return Cache[name] = buffer.ToArray();
        }
    }
}
