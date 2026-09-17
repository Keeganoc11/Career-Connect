using CareerConnect.Api.Domain;
using CareerConnect.Api.Services;
using UglyToad.PdfPig;

namespace CareerConnect.Api.Tests;

/// <summary>Bullets that wrap, heading rules and colored links — the shape of a Word-exported resume.</summary>
public class WrappedResumeTests
{
    private readonly ResumeRenderer _renderer = new();
    private readonly ResumeLayout _layout = TestResumes.WrappedLayout();

    private ResumeLayout RoundTrip(ResumeLayout layout) =>
        Assert.IsType<ResumeLayoutReadOutcome.Success>(new ResumeLayoutReader().Read(_renderer.Render(layout))).Layout;

    private const string Fill =
        "Supported production Linux infrastructure through server provisioning, imaging, and hardware lifecycle management across a live data center";

    [Fact]
    public void Reader_FoldsAWrappedRowIntoItsBullet()
    {
        var read = RoundTrip(_layout);

        var bullet = read.Find(TestResumes.WrappedBulletLine)!;
        Assert.Equal(2, bullet.RowCount);
        Assert.EndsWith("hardware lifecycle management in a live data center environment", bullet.EditableText);
        Assert.DoesNotContain(read.Lines, l => l.Text == "management in a live data center environment");
    }

    [Fact]
    public void Reader_DoesNotFoldPlainTextThatStartsAtTheMargin()
    {
        var read = RoundTrip(_layout);

        Assert.Contains(read.Lines, l => l.Text.StartsWith("Worked across the platform team"));
        // Ids follow printed rows, so the bullet after a two-row one reads back one number later.
        Assert.Equal(1, read.Lines.Single(l => l.Text.Contains("Tracked incidents")).RowCount);
    }

    [Fact]
    public void Reader_KeepsRulesAndLinkColor()
    {
        var read = RoundTrip(_layout);

        Assert.Equal(3, read.Rules.Count);
        Assert.Contains(read.Rules, r => r.Width < 0.4 && r.Color.IsBlack);
        var link = read.Find("L02")!.Runs.Single(r => r.Text.Contains("github"));
        Assert.NotNull(link.Color);
        Assert.InRange(link.Color!.B, 0.75, 0.85);
    }

    [Fact]
    public void Measure_ARewriteMustFillExactlyTheRowsTheBulletHad()
    {
        var line = _layout.Find(TestResumes.WrappedBulletLine)!;

        var fits = _renderer.MeasureLine(_layout, line.WithEditableText(Fill));
        var tooShort = _renderer.MeasureLine(_layout, line.WithEditableText("Supported production infrastructure"));
        var tooLong = _renderer.MeasureLine(_layout, line.WithEditableText(string.Join(' ', Enumerable.Repeat(Fill, 2))));

        Assert.True(fits.Fits);
        Assert.False(fits.LeavesGap);
        Assert.True(tooShort.LeavesGap);
        Assert.False(tooLong.Fits);
        Assert.Equal(2, tooLong.RowsAvailable);
    }

    [Fact]
    public void Render_WrapsARewriteAcrossTheOriginalRows_AndDropsTheUnderlineBeneathIt()
    {
        var edited = _layout.WithLine(_layout.Find(TestResumes.WrappedBulletLine)!.WithEditableText(Fill));

        var read = RoundTrip(edited);

        var bullet = read.Find(TestResumes.WrappedBulletLine)!;
        Assert.Equal(2, bullet.RowCount);
        Assert.Equal(Fill, bullet.EditableText);
        Assert.Equal(_layout.Find("L07")!.Baseline, read.Lines.Single(l => l.Text.Contains("Tracked incidents")).Baseline, 1);
        // The heading rule and the contact-line underline stay; the underline under the rewritten words goes.
        Assert.Equal(2, read.Rules.Count);
    }

    [Fact]
    public void Render_UnchangedWrappedLayout_KeepsItsText()
    {
        using var pdf = PdfDocument.Open(_renderer.Render(_layout));

        var text = pdf.GetPage(1).Text;
        Assert.Contains("hardware lifecycle", text);
        Assert.Contains("management in a live data center environment", text);
    }

    [Fact]
    public void CharacterBudget_GivesAWrappedBulletAMinimumAsWellAsAMaximum()
    {
        var budget = _renderer.CharacterBudget(_layout, _layout.Find(TestResumes.WrappedBulletLine)!);

        Assert.InRange(budget.Min, 70, 110);
        Assert.InRange(budget.Max, 150, 200);
    }

    [Fact]
    public async Task Guard_SendsBackARewriteThatLeavesARowEmpty_AndKeepsTheFilledVersion()
    {
        var tailorer = new FakeResumeLayoutTailorer { Fit = l => l.TooShort ? Fill : l.Text };
        var guard = new ResumeEditGuard(_renderer, tailorer);

        var result = await guard.ApplyAsync(_layout, _layout,
            [new LineEdit(TestResumes.WrappedBulletLine, "Supported production infrastructure", "r")],
            new TailorContext("A role.", "Engineer", "Acme", null));

        Assert.Equal(1, tailorer.FitCallCount);
        Assert.Equal(Fill, result.Layout.Find(TestResumes.WrappedBulletLine)!.EditableText);
        Assert.Single(result.Applied);
    }

    [Fact]
    public async Task Guard_PutsTheOriginalBack_WhenARewriteNeverFillsItsRows()
    {
        var tailorer = new FakeResumeLayoutTailorer { Fit = l => l.Text };
        var guard = new ResumeEditGuard(_renderer, tailorer);

        var result = await guard.ApplyAsync(_layout, _layout,
            [new LineEdit(TestResumes.WrappedBulletLine, "Supported production infrastructure", "r")],
            new TailorContext("A role.", "Engineer", "Acme", null));

        Assert.Contains("Too short to fill its 2 lines", Assert.Single(result.Rejected).Why);
        Assert.Equal(_layout.Find(TestResumes.WrappedBulletLine)!.EditableText, result.Layout.Find(TestResumes.WrappedBulletLine)!.EditableText);
    }

    [Fact]
    public void Layout_StoredBeforeWrappingExisted_StillLoads()
    {
        const string json = """{"pageWidth":612,"pageHeight":792,"rightLimit":540,"lines":[{"id":"L01","kind":"Bullet","baseline":700,"runs":[{"text":"x","family":"Serif","bold":false,"italic":false,"fontSize":11,"x":108}],"editableFrom":0}],"links":[]}""";
        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        };

        var layout = System.Text.Json.JsonSerializer.Deserialize<ResumeLayout>(json, options)!;

        Assert.Empty(layout.Rules);
        Assert.Equal(1, layout.Lines[0].RowCount);
        Assert.Null(layout.Lines[0].Runs[0].Color);
    }
}
