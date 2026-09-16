using CareerConnect.Api.Domain;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;

namespace CareerConnect.Api.Services;

/// <summary>How far one line reaches, measured with the fonts it will actually be drawn in.</summary>
public record LineMeasurement(string LineId, double EndX, double Limit)
{
    public bool Fits => EndX <= Limit + FitTolerance;

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
    /// Roughly how many characters of this line's typical text fit before the
    /// margin — a budget to hand a model, which can't measure points itself.
    /// The real check is always <see cref="MeasureEditableLines"/>.
    /// </summary>
    int CharacterBudget(ResumeLayout layout, ResumeLine line);
}

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
            var runs = session.Place(line);
            for (var i = 0; i < runs.Count; i++)
            {
                session.Draw(runs[i], line.Baseline, isLastOnLine: i == runs.Count - 1);
            }
        }

        foreach (var link in layout.Links)
        {
            session.Page.AddLink(link.Uri, new PdfRectangle(link.X, link.Y, link.X + link.Width, link.Y + link.Height));
        }

        return session.Builder.Build();
    }

    public IReadOnlyList<LineMeasurement> MeasureEditableLines(ResumeLayout layout)
    {
        using var session = new Session(layout.PageWidth, layout.PageHeight);

        return layout.Lines
            .Where(l => l.Editable)
            .Select(l => new LineMeasurement(l.Id, session.EndOf(l), layout.RightLimit))
            .ToList();
    }

    public LineMeasurement MeasureLine(ResumeLayout layout, ResumeLine line)
    {
        using var session = new Session(layout.PageWidth, layout.PageHeight);
        return new LineMeasurement(line.Id, session.EndOf(line), layout.RightLimit);
    }

    public int CharacterBudget(ResumeLayout layout, ResumeLine line)
    {
        if (line.EditableFrom is not { } from || line.EditableText is not { Length: > 0 } text)
        {
            return 0;
        }

        using var session = new Session(layout.PageWidth, layout.PageHeight);

        var run = line.Runs[from];
        var width = session.EndOf(run with { Text = text }) - run.X;
        if (width <= 0)
        {
            return text.Length;
        }

        var perCharacter = width / text.Length;
        return (int)Math.Floor((layout.RightLimit - run.X) / perCharacter);
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
                Page.AddText(text, run.FontSize, new PdfPoint(run.X, baseline), Font(run));
            }
            catch (Exception ex) when (ex is not ResumeRenderException)
            {
                throw new ResumeRenderException($"Couldn't draw \"{text}\" — it may contain a character the resume font doesn't have.", ex);
            }
        }

        public double EndOf(ResumeLine line)
        {
            var runs = Place(line);
            return runs.Count == 0 ? 0 : EndOf(runs[^1]);
        }

        /// <summary>
        /// Each run at its original x, unless the run before it (with its
        /// trailing space) now reaches past that point. Tinos Bold runs a
        /// fraction wider than some Times renderings, and without this a date
        /// after a bold title can touch the title's last word.
        /// </summary>
        public List<ResumeRun> Place(ResumeLine line)
        {
            var placed = new List<ResumeRun>(line.Runs.Count);
            foreach (var run in line.Runs)
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
