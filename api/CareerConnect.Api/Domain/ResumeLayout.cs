using System.Text.Json.Serialization;

namespace CareerConnect.Api.Domain;

public enum ResumeFontFamily
{
    /// <summary>Times New Roman in the source, drawn with Tinos — same metrics, so lines break identically.</summary>
    Serif,

    /// <summary>Arial in the source, drawn with Arimo.</summary>
    Sans,
}

public enum ResumeLineKind
{
    /// <summary>An empty paragraph. It still holds its place, because spacing is part of the format.</summary>
    Blank,
    Heading,
    Bullet,
    /// <summary>"Category: items" under a skills heading — the category is fixed, the items are not.</summary>
    Skill,
    /// <summary>Anything else: the name, contact lines, entry titles, organizations, education.</summary>
    Text,
}

/// <summary>An RGB color, each channel 0–1 as PDFs store it.</summary>
public record ResumeColor(double R, double G, double B)
{
    public bool IsBlack => R < 0.02 && G < 0.02 && B < 0.02;
}

/// <summary>A stretch of one line drawn in one font, starting at the x it had in the uploaded PDF.</summary>
/// <param name="Color">Null for black, the overwhelmingly common case. Links are often blue.</param>
public record ResumeRun(
    string Text, ResumeFontFamily Family, bool Bold, bool Italic, double FontSize, double X, ResumeColor? Color = null);

/// <summary>
/// A straight line drawn on the page — a rule under a section heading, the
/// underline beneath a link. Part of the format, so it's redrawn exactly.
/// </summary>
public record ResumeRule(double X1, double Y1, double X2, double Y2, double Width, ResumeColor Color);

/// <summary>A clickable area carried over from the uploaded PDF, in page coordinates.</summary>
public record ResumeLink(string Uri, double X, double Y, double Width, double Height);

/// <summary>One further printed row of a paragraph that wraps, as it sat in the uploaded PDF.</summary>
public record ResumeRow(double Baseline, List<ResumeRun> Runs);

/// <summary>
/// One paragraph of the resume — usually one printed row, sometimes several
/// when a bullet or skill list wraps. However many rows it had in the upload,
/// it keeps exactly that many: a rewrite fills the same rows, so nothing below
/// it moves and the page can't grow.
/// </summary>
public class ResumeLine
{
    /// <summary>Stable within one layout ("L07"), so a model can refer to a line and nothing else.</summary>
    public required string Id { get; init; }

    public required ResumeLineKind Kind { get; init; }

    /// <summary>Baseline y of the first row, from the uploaded PDF. Never recomputed — this is what keeps the spacing identical.</summary>
    public required double Baseline { get; init; }

    /// <summary>The first row's runs.</summary>
    public required List<ResumeRun> Runs { get; init; }

    /// <summary>Rows after the first, for a paragraph that wraps. Empty for the common one-row line.</summary>
    public List<ResumeRow> Continuations { get; init; } = [];

    /// <summary>
    /// Set once tailoring has replaced this line's words. A link underline
    /// drawn beneath the original words no longer lines up, so the renderer
    /// leaves out any underline under an edited line's words.
    /// </summary>
    public bool Edited { get; init; }

    /// <summary>
    /// Runs from this index on are the words tailoring may replace; everything
    /// before it (a bullet glyph, a skill category) is fixed. Null means the
    /// whole line is locked.
    /// </summary>
    public int? EditableFrom { get; init; }

    /// <summary>
    /// New words for a wrapped paragraph. They can't be stored as runs, because
    /// where they break across rows depends on measuring them — the renderer
    /// wraps them into the original rows when it draws. Null until tailored.
    /// </summary>
    public string? Replacement { get; init; }

    [JsonIgnore]
    public bool Editable => EditableFrom is not null;

    [JsonIgnore]
    public int RowCount => 1 + Continuations.Count;

    [JsonIgnore]
    public string Text => Replacement is not null
        ? $"{Prefix}{Replacement}".TrimEnd()
        : string.Join(' ', AllRows().Select(RowText).Where(t => t.Length > 0));

    /// <summary>The replaceable words alone, across every row, or null for a locked line.</summary>
    [JsonIgnore]
    public string? EditableText => EditableFrom is { } from
        ? Replacement ?? string.Join(' ', new[] { string.Concat(Runs.Skip(from).Select(r => r.Text)).Trim() }
            .Concat(Continuations.Select(c => RowText(c.Runs)))
            .Where(t => t.Length > 0))
        : null;

    /// <summary>The fixed part before the editable words — a bullet glyph, a "Backend: " label.</summary>
    [JsonIgnore]
    public string Prefix => EditableFrom is { } from ? string.Concat(Runs.Take(from).Select(r => r.Text)) : "";

    /// <summary>
    /// The same line with its editable words swapped. The new words take the
    /// font and starting x of the first run they replace, so nothing before
    /// them moves.
    /// </summary>
    public ResumeLine WithEditableText(string text)
    {
        if (EditableFrom is not { } from)
        {
            throw new InvalidOperationException($"Line {Id} is locked.");
        }

        if (Continuations.Count > 0)
        {
            return new ResumeLine
            {
                Id = Id,
                Kind = Kind,
                Baseline = Baseline,
                EditableFrom = from,
                Runs = Runs,
                Continuations = Continuations,
                Replacement = text,
                Edited = true,
            };
        }

        var first = Runs[from];
        return new ResumeLine
        {
            Id = Id,
            Kind = Kind,
            Baseline = Baseline,
            EditableFrom = from,
            Runs = [.. Runs.Take(from), first with { Text = text }],
            Edited = true,
        };
    }

    private IEnumerable<List<ResumeRun>> AllRows() => new[] { Runs }.Concat(Continuations.Select(c => c.Runs));

    private static string RowText(List<ResumeRun> runs) => string.Concat(runs.Select(r => r.Text)).Trim();
}

/// <summary>
/// A one-page resume as positioned lines, read from the PDF the user uploaded.
///
/// Tailoring changes words and nothing else: every line keeps its baseline,
/// fonts and starting x, and no line is ever added or removed. That is what
/// makes "never change the format" and "never two pages" structural
/// guarantees rather than things a model is asked nicely to respect.
/// </summary>
public class ResumeLayout
{
    public required double PageWidth { get; init; }
    public required double PageHeight { get; init; }

    /// <summary>Furthest x any line may reach — the left margin mirrored on the right.</summary>
    public required double RightLimit { get; init; }

    public required List<ResumeLine> Lines { get; init; }

    public List<ResumeLink> Links { get; init; } = [];

    public List<ResumeRule> Rules { get; init; } = [];

    public ResumeLine? Find(string id) => Lines.FirstOrDefault(l => l.Id == id);

    public ResumeLayout WithLine(ResumeLine replacement) => new()
    {
        PageWidth = PageWidth,
        PageHeight = PageHeight,
        RightLimit = RightLimit,
        Links = Links,
        Rules = Rules,
        Lines = Lines.Select(l => l.Id == replacement.Id ? replacement : l).ToList(),
    };

    /// <summary>Plain text in reading order, for scoring and for prompts.</summary>
    public string ToPlainText() =>
        string.Join('\n', Lines
            .Where(l => l.Kind != ResumeLineKind.Blank)
            .Select(l => l.Text));
}
