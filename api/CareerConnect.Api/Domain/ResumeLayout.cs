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

/// <summary>A stretch of one line drawn in one font, starting at the x it had in the uploaded PDF.</summary>
public record ResumeRun(string Text, ResumeFontFamily Family, bool Bold, bool Italic, double FontSize, double X);

/// <summary>A clickable area carried over from the uploaded PDF, in page coordinates.</summary>
public record ResumeLink(string Uri, double X, double Y, double Width, double Height);

public class ResumeLine
{
    /// <summary>Stable within one layout ("L07"), so a model can refer to a line and nothing else.</summary>
    public required string Id { get; init; }

    public required ResumeLineKind Kind { get; init; }

    /// <summary>Baseline y from the uploaded PDF. Never recomputed — this is what keeps the spacing identical.</summary>
    public required double Baseline { get; init; }

    public required List<ResumeRun> Runs { get; init; }

    /// <summary>
    /// Runs from this index on are the words tailoring may replace; everything
    /// before it (a bullet glyph, a skill category) is fixed. Null means the
    /// whole line is locked.
    /// </summary>
    public int? EditableFrom { get; init; }

    [JsonIgnore]
    public bool Editable => EditableFrom is not null;

    [JsonIgnore]
    public string Text => string.Concat(Runs.Select(r => r.Text)).TrimEnd();

    /// <summary>The replaceable words alone, or null for a locked line.</summary>
    [JsonIgnore]
    public string? EditableText => EditableFrom is { } from
        ? string.Concat(Runs.Skip(from).Select(r => r.Text)).Trim()
        : null;

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

        var first = Runs[from];
        return new ResumeLine
        {
            Id = Id,
            Kind = Kind,
            Baseline = Baseline,
            EditableFrom = from,
            Runs = [.. Runs.Take(from), first with { Text = text }],
        };
    }
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

    public ResumeLine? Find(string id) => Lines.FirstOrDefault(l => l.Id == id);

    public ResumeLayout WithLine(ResumeLine replacement) => new()
    {
        PageWidth = PageWidth,
        PageHeight = PageHeight,
        RightLimit = RightLimit,
        Links = Links,
        Lines = Lines.Select(l => l.Id == replacement.Id ? replacement : l).ToList(),
    };

    /// <summary>Plain text in reading order, for scoring and for prompts.</summary>
    public string ToPlainText() =>
        string.Join('\n', Lines
            .Where(l => l.Kind != ResumeLineKind.Blank)
            .Select(l => l.Text));
}
