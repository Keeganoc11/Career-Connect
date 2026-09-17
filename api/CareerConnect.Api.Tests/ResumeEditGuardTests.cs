using CareerConnect.Api.Domain;
using CareerConnect.Api.Services;

namespace CareerConnect.Api.Tests;

public class ResumeEditGuardTests
{
    private readonly FakeResumeLayoutTailorer _tailorer = new();
    private readonly ResumeEditGuard _guard;
    private readonly ResumeLayout _base = TestResumes.Layout();
    private readonly TailorContext _context = new("A backend role.", "Software Engineer", "Acme", ExtraFacts: null);

    public ResumeEditGuardTests()
    {
        _guard = new ResumeEditGuard(new ResumeRenderer(), _tailorer);
    }

    private Task<GuardedEdits> Apply(params LineEdit[] edits) =>
        _guard.ApplyAsync(_base, _base, edits, _context);

    [Fact]
    public async Task ApplyAsync_AppliesAnEditThatPassesEveryRule()
    {
        var result = await Apply(new LineEdit(TestResumes.BulletLine, "Built a REST inventory API in ASP.NET Core serving 3 internal teams", "Leads with REST."));

        var applied = Assert.Single(result.Applied);
        Assert.Equal("Leads with REST.", applied.Reason);
        Assert.Equal("Built a REST inventory API in ASP.NET Core serving 3 internal teams", result.Layout.Find(TestResumes.BulletLine)!.EditableText);
        Assert.Empty(result.Rejected);
    }

    [Fact]
    public async Task ApplyAsync_NeverChangesTheShapeOfThePage()
    {
        var result = await Apply(
            new LineEdit(TestResumes.BulletLine, "Delivered an ASP.NET Core inventory API", "r"),
            new LineEdit(TestResumes.SkillLine, "ASP.NET Core, C#, REST APIs, SQL Server", "r"));

        Assert.Equal(_base.Lines.Select(l => (l.Id, l.Kind, l.Baseline)), result.Layout.Lines.Select(l => (l.Id, l.Kind, l.Baseline)));
        Assert.Equal("Backend: ASP.NET Core, C#, REST APIs, SQL Server", result.Layout.Find(TestResumes.SkillLine)!.Text);
    }

    [Fact]
    public async Task ApplyAsync_RejectsEditsToLockedLines()
    {
        var result = await Apply(new LineEdit("L01", "Someone Else", "r"), new LineEdit(TestResumes.LinkBulletLine, "https://evil.example", "r"));

        Assert.Empty(result.Applied);
        Assert.Equal(2, result.Rejected.Count);
        Assert.Equal(_base.ToPlainText(), result.Layout.ToPlainText());
    }

    [Fact]
    public async Task ApplyAsync_RejectsBracketedPlaceholders()
    {
        var result = await Apply(new LineEdit(TestResumes.BulletLine, "Built an API that improved [specific metric]", "r"));

        Assert.Contains("placeholder", Assert.Single(result.Rejected).Why);
    }

    [Fact]
    public async Task ApplyAsync_RejectsNumbersThatArentInTheResume()
    {
        var result = await Apply(new LineEdit(TestResumes.BulletLine, "Built an inventory API serving 12 teams and 40% faster", "r"));

        var rejected = Assert.Single(result.Rejected);
        Assert.Contains("12", rejected.Why);
        Assert.Contains("40", rejected.Why);
    }

    [Fact]
    public async Task ApplyAsync_AllowsNumbersFromTheResumeOrExtraFacts()
    {
        var context = _context with { ExtraFacts = "The API handled 40 requests per second at peak." };

        var result = await _guard.ApplyAsync(_base, _base,
            [new LineEdit(TestResumes.BulletLine, "Built an ASP.NET Core API for 3 teams at 40 requests/sec", "r")], context);

        Assert.Single(result.Applied);
    }

    [Fact]
    public async Task ApplyAsync_StripsWhatAModelAddsAroundTheWords()
    {
        var result = await Apply(new LineEdit(TestResumes.BulletLine, "  ● \"Built   an **inventory** API\"  ", "r"));

        Assert.Equal("Built an inventory API", result.Layout.Find(TestResumes.BulletLine)!.EditableText);
    }

    [Fact]
    public async Task ApplyAsync_ShortensALineThatRunsPastTheMargin()
    {
        var tooLong = string.Join(' ', Enumerable.Repeat("Built ASP.NET Core APIs", 8));
        _tailorer.Fit = _ => "Built ASP.NET Core APIs";

        var result = await Apply(new LineEdit(TestResumes.BulletLine, tooLong, "r"));

        Assert.Equal(1, _tailorer.FitCallCount);
        Assert.Equal("Built ASP.NET Core APIs", result.Layout.Find(TestResumes.BulletLine)!.EditableText);
        Assert.Single(result.Applied);
    }

    [Fact]
    public async Task ApplyAsync_KeepsTheOriginalWords_WhenALineStillWontFit()
    {
        var tooLong = string.Join(' ', Enumerable.Repeat("Built ASP.NET Core APIs", 8));
        _tailorer.Fit = l => l.Text; // never actually gets shorter

        var result = await Apply(new LineEdit(TestResumes.BulletLine, tooLong, "r"));

        Assert.Equal(2, _tailorer.FitCallCount);
        Assert.Empty(result.Applied);
        Assert.Contains("one line", Assert.Single(result.Rejected).Why);
        Assert.Equal(_base.Find(TestResumes.BulletLine)!.Text, result.Layout.Find(TestResumes.BulletLine)!.Text);
    }

    [Fact]
    public async Task ApplyAsync_SkipsEditsThatChangeNothing()
    {
        var result = await Apply(new LineEdit(TestResumes.BulletLine, _base.Find(TestResumes.BulletLine)!.EditableText!, "r"));

        Assert.Empty(result.Applied);
        Assert.Empty(result.Rejected);
    }
}
