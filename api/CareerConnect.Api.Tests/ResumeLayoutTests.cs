using CareerConnect.Api.Domain;
using CareerConnect.Api.Services;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace CareerConnect.Api.Tests;

public class ResumeLayoutTests
{
    private readonly ResumeRenderer _renderer = new();
    private readonly ResumeLayoutReader _reader = new();

    private ResumeLayout RoundTrip(ResumeLayout layout) =>
        Assert.IsType<ResumeLayoutReadOutcome.Success>(_reader.Read(_renderer.Render(layout))).Layout;

    [Fact]
    public void Render_ProducesOnePageWithEveryLineInOrder()
    {
        var layout = TestResumes.Layout();

        using var pdf = PdfDocument.Open(_renderer.Render(layout));

        Assert.Equal(1, pdf.NumberOfPages);
        var text = pdf.GetPage(1).Text;
        var positions = layout.Lines.Select(l => text.IndexOf(l.Text, StringComparison.Ordinal)).ToList();
        Assert.DoesNotContain(-1, positions);
        Assert.Equal(positions.OrderBy(p => p), positions);
    }

    [Fact]
    public void Render_KeepsTheSpaceBetweenRunsSoExtractedTextReadsAsWords()
    {
        using var pdf = PdfDocument.Open(_renderer.Render(TestResumes.Layout()));

        Assert.Contains("Backend: C#", pdf.GetPage(1).Text);
    }

    [Fact]
    public void Render_CarriesLinksOver()
    {
        using var pdf = PdfDocument.Open(_renderer.Render(TestResumes.Layout()));

        var link = Assert.Single(pdf.GetPage(1).GetAnnotations());
        Assert.Equal(TestResumes.LinkUri, ((UglyToad.PdfPig.Actions.UriAction)link.Action!).Uri);
    }

    [Fact]
    public void Reader_RecoversLinesKindsAndPositionsFromARenderedResume()
    {
        var original = TestResumes.Layout();

        var read = RoundTrip(original);

        Assert.Equal(original.Lines.Select(l => l.Text), read.Lines.Select(l => l.Text));
        Assert.Equal(original.Lines.Select(l => l.Kind), read.Lines.Select(l => l.Kind));
        Assert.Equal(original.Lines.Select(l => Math.Round(l.Baseline, 2)), read.Lines.Select(l => Math.Round(l.Baseline, 2)));
        Assert.Equal(540, read.RightLimit);
        Assert.Single(read.Links);
    }

    [Fact]
    public void Reader_MakesBulletsAndSkillItemsEditable_ButLocksEverythingElse()
    {
        var read = RoundTrip(TestResumes.Layout());

        Assert.Equal(
            [TestResumes.SkillLine, TestResumes.BulletLine, TestResumes.SecondBulletLine],
            read.Lines.Where(l => l.Editable).Select(l => l.Id));
        Assert.Equal("C#, ASP.NET Core, SQL Server, REST APIs", read.Find(TestResumes.SkillLine)!.EditableText);
        Assert.False(read.Find(TestResumes.LinkBulletLine)!.Editable);
    }

    [Fact]
    public void Reader_RejectsAMultiPagePdf()
    {
        using var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.TimesRoman);
        for (var i = 0; i < 2; i++)
        {
            var page = builder.AddPage(612, 792);
            page.AddText(new string('x', 80), 11, new PdfPoint(72, 700), font);
        }

        var outcome = _reader.Read(builder.Build());

        Assert.Contains("2 pages", Assert.IsType<ResumeLayoutReadOutcome.Failed>(outcome).Message);
    }

    [Fact]
    public void Reader_RejectsFontsItCantReproduce()
    {
        using var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Courier);
        var page = builder.AddPage(612, 792);
        page.AddText(new string('x', 80), 11, new PdfPoint(72, 700), font);

        var outcome = _reader.Read(builder.Build());

        Assert.Contains("Courier", Assert.IsType<ResumeLayoutReadOutcome.Failed>(outcome).Message);
    }

    [Fact]
    public void Reader_RejectsSomethingThatIsNotAPdf()
    {
        Assert.IsType<ResumeLayoutReadOutcome.Failed>(_reader.Read("not a pdf"u8.ToArray()));
    }

    [Fact]
    public void WithEditableText_ReplacesOnlyTheWordsAndKeepsWhereTheyStart()
    {
        var line = TestResumes.Layout().Find(TestResumes.SkillLine)!;

        var edited = line.WithEditableText("C#, .NET, PostgreSQL");

        Assert.Equal("Backend: C#, .NET, PostgreSQL", edited.Text);
        Assert.Equal(line.Runs[1].X, edited.Runs[1].X);
        Assert.Equal(line.Baseline, edited.Baseline);
    }

    [Fact]
    public void WithEditableText_RefusesALockedLine()
    {
        var line = TestResumes.Layout().Find("L01")!;

        Assert.Throws<InvalidOperationException>(() => line.WithEditableText("Someone Else"));
    }

    [Fact]
    public void MeasureLine_FlagsTextThatWouldRunPastTheMargin()
    {
        var layout = TestResumes.Layout();
        var line = layout.Find(TestResumes.BulletLine)!;

        Assert.True(_renderer.MeasureLine(layout, line).Fits);
        Assert.False(_renderer.MeasureLine(layout, line.WithEditableText(new string('w', 120))).Fits);
    }

    [Fact]
    public void CharacterBudget_IsRoughlyWhatFitsBeforeTheMargin()
    {
        var layout = TestResumes.Layout();

        var budget = _renderer.CharacterBudget(layout, layout.Find(TestResumes.BulletLine)!);

        Assert.InRange(budget, 70, 110);
    }

    [Theory]
    [InlineData(85, 0, FitVerdict.StrongFit)]
    [InlineData(70, 0, FitVerdict.WorthAShot)]
    [InlineData(55, 0, FitVerdict.Stretch)]
    [InlineData(30, 0, FitVerdict.NotAFit)]
    // A dealbreaker caps the verdict however well the rewrite scored.
    [InlineData(92, 1, FitVerdict.Stretch)]
    [InlineData(30, 1, FitVerdict.NotAFit)]
    [InlineData(92, 2, FitVerdict.NotAFit)]
    public void FitVerdicts_FollowTheScoreBands_AndDealbreakersCapThem(int score, int dealbreakers, FitVerdict expected)
    {
        Assert.Equal(expected, FitVerdicts.From(score, dealbreakers));
    }
}
