using CareerConnect.Api.Domain;

namespace CareerConnect.Api.Tests;

/// <summary>
/// A small, made-up one-page resume laid out the way a Google Docs export is:
/// 11pt serif, 1" margins, sans bullets indented under bold entry titles.
/// </summary>
public static class TestResumes
{
    public const string BulletLine = "L06";
    public const string SecondBulletLine = "L07";
    public const string SkillLine = "L04";
    public const string LinkBulletLine = "L08";
    public const string LinkUri = "https://example.com/portfolio";

    public static ResumeLayout Layout()
    {
        var y = 700.0;
        var index = 0;
        var lines = new List<ResumeLine>();

        ResumeLine Add(ResumeLineKind kind, List<ResumeRun> runs, int? editableFrom = null)
        {
            var line = new ResumeLine
            {
                Id = $"L{++index:00}",
                Kind = kind,
                Baseline = y,
                Runs = runs,
                EditableFrom = editableFrom,
            };
            lines.Add(line);
            y -= 13.95;
            return line;
        }

        static ResumeRun Serif(string text, double x, bool bold = false, bool italic = false, double size = 11) =>
            new(text, ResumeFontFamily.Serif, bold, italic, size, x);

        static ResumeRun Bullet() => new("●  ", ResumeFontFamily.Sans, false, false, 11, 90);

        Add(ResumeLineKind.Text, [Serif("Jordan Rivera", 250, bold: true, size: 18)]);
        Add(ResumeLineKind.Text, [Serif("(555) 010-0199 ● Springfield, IL ● jordan@example.com", 170)]);
        Add(ResumeLineKind.Heading, [Serif("SKILLS", 72, bold: true)]);
        Add(ResumeLineKind.Skill, [Serif("Backend: ", 72, bold: true), Serif("C#, ASP.NET Core, SQL Server, REST APIs", 119.33)], editableFrom: 1);
        Add(ResumeLineKind.Text, [Serif("Inventory Service ", 72, bold: true), Serif("March 2025 - Present", 170, italic: true)]);
        Add(ResumeLineKind.Bullet, [Bullet(), Serif("Built an inventory API in ASP.NET Core serving 3 internal teams", 108)], editableFrom: 1);
        Add(ResumeLineKind.Bullet, [Bullet(), Serif("Wrote unit tests with xUnit and cut regressions in half", 108)], editableFrom: 1);
        Add(ResumeLineKind.Bullet, [Bullet(), Serif(LinkUri, 108)]);
        Add(ResumeLineKind.Heading, [Serif("EDUCATION", 72, bold: true)]);
        Add(ResumeLineKind.Text, [Serif("Bachelor of Science in Computer Science, May 2024", 72)]);

        // Enough body text that a reader treats it as a real document.
        return new ResumeLayout
        {
            PageWidth = 612,
            PageHeight = 792,
            RightLimit = 540,
            Lines = lines,
            Links = [new ResumeLink(LinkUri, 108, lines[7].Baseline - 3, 150, 12.7)],
        };
    }

    public const string WrappedBulletLine = "L06";

    /// <summary>
    /// A resume shaped like a Word export: a rule under the heading, a blue
    /// link, and a bullet that wraps onto a second row at the text indent.
    /// </summary>
    public static ResumeLayout WrappedLayout()
    {
        static ResumeRun Serif(string text, double x, bool bold = false, ResumeColor? color = null) =>
            new(text, ResumeFontFamily.Serif, bold, false, 11, x, color);

        var blue = new ResumeColor(0.0667, 0.3333, 0.8);
        ResumeLine Line(int n, ResumeLineKind kind, double y, List<ResumeRun> runs, int? editableFrom = null, List<ResumeRow>? continuations = null) => new()
        {
            Id = $"L{n:00}",
            Kind = kind,
            Baseline = y,
            Runs = runs,
            EditableFrom = editableFrom,
            Continuations = continuations ?? [],
        };

        return new ResumeLayout
        {
            PageWidth = 612,
            PageHeight = 792,
            RightLimit = 540,
            Lines =
            [
                Line(1, ResumeLineKind.Text, 700, [Serif("Jordan Rivera", 250, bold: true)]),
                Line(2, ResumeLineKind.Text, 686, [Serif("jordan@example.com ", 200), Serif("github.com/jordan", 300, color: blue)]),
                Line(3, ResumeLineKind.Heading, 660, [Serif("WORK EXPERIENCE", 72, bold: true)]),
                Line(4, ResumeLineKind.Text, 647, [Serif("Platform Engineer ", 72, bold: true), Serif("Globex | Springfield", 190)]),
                Line(5, ResumeLineKind.Text, 634, [Serif("Worked across the platform team for two years on internal tooling", 72)]),
                Line(6, ResumeLineKind.Bullet, 621,
                    [new ResumeRun("●  ", ResumeFontFamily.Sans, false, false, 11, 90), Serif("Supported production infrastructure through server provisioning, imaging, and hardware lifecycle", 108)],
                    editableFrom: 1,
                    continuations: [new ResumeRow(608, [Serif("management in a live data center environment", 108)])]),
                Line(7, ResumeLineKind.Bullet, 595,
                    [new ResumeRun("●  ", ResumeFontFamily.Sans, false, false, 11, 90), Serif("Tracked incidents in Jira with the engineering team", 108)],
                    editableFrom: 1),
            ],
            Rules =
            [
                new ResumeRule(72, 658, 540, 658, 0.375, new ResumeColor(0, 0, 0)),
                new ResumeRule(300, 684.5, 390, 684.5, 0.54, blue),
                new ResumeRule(108, 606.5, 350, 606.5, 0.54, blue),
            ],
        };
    }
}
