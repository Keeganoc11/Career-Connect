using CareerConnect.Api.Domain;
using CareerConnect.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CareerConnect.Api.Tests;

public class EntrySwapTests
{
    private readonly ResumeRenderer _renderer = new();
    private readonly ResumeLayout _layout = TestResumes.SwapLayout();
    private readonly TailorContext _context = new("Backend .NET role.", "Software Engineer", "Globex", TestResumes.SwapFacts);

    private static EntrySwapProposal ProjectSwap(
        string title = "Career Connect Application",
        string dates = "Aug 2026 - Present",
        string? secondBullet = "Wrote 257 xUnit tests and deployed it on Railway with Docker") => new(
        "S11",
        "Career Connect Application",
        [new HeaderLineFields("L11", [title, dates])],
        [
            new LineEdit("L12", "Built a full-stack job tracker with ASP.NET Core, EF Core, PostgreSQL and React", ""),
            new LineEdit("L13", secondBullet ?? "", ""),
        ],
        "The posting is backend .NET work, which Career Connect shows directly.");

    private static EntrySwapProposal InternSwap(string dates = "Sept 2025 - Dec 2025", string company = "Acme | Springfield, IL") => new(
        "S06",
        "Software Engineering Intern",
        [new HeaderLineFields("L06", ["Software Engineering Intern", dates]), new HeaderLineFields("L07", [company])],
        [
            new LineEdit("L08", "Built internal REST APIs in C# for internal teams", ""),
            new LineEdit("L09", "Wrote integration tests for the billing service", ""),
        ],
        "A software internship reads closer to this role than IT support.");

    private Task<GuardedEdits> Apply(EntrySwapProposal swap, FakeResumeLayoutTailorer? tailorer = null, TailorContext? context = null) =>
        new ResumeEditGuard(_renderer, tailorer ?? new FakeResumeLayoutTailorer { Fit = l => l.Text })
            .ApplyAsync(_layout, _layout, [], context ?? _context, [swap]);

    [Fact]
    public void Find_GroupsHeadingsAndBulletsIntoSlotsPerSection()
    {
        var entries = ResumeEntries.Find(_layout);

        Assert.Equal(["S02", "S06", "S11"], entries.Select(e => e.SlotId));
        Assert.Equal([EntrySection.Work, EntrySection.Work, EntrySection.Projects], entries.Select(e => e.Section));
        Assert.Equal(["L02", "L03"], entries[0].HeaderLineIds);
        Assert.Equal(["L12", "L13"], entries[2].BulletLineIds);
    }

    [Theory]
    [InlineData("January 2026 – July 2026", 2026 * 12 + 1, 2026 * 12 + 7)]
    [InlineData("Nov 2025 - Present", 2025 * 12 + 11, int.MaxValue)]
    [InlineData("Sept 2025 - Dec 2025", 2025 * 12 + 9, 2025 * 12 + 12)]
    [InlineData("2024", 2024 * 12 + 12, 2024 * 12 + 12)]
    public void DateRange_ReadsCommonDateStyles(string text, int start, int end)
    {
        Assert.Equal((start, end), ResumeEntries.DateRange(text));
    }

    [Fact]
    public async Task Swap_FillsTheProjectSlot_AndItsDatesFollowTheNewTitle()
    {
        var result = await Apply(ProjectSwap());

        var swap = Assert.Single(result.Swaps);
        Assert.Equal("Drinks Around The World IOS Application → Career Connect Application", swap.Label);
        var heading = result.Layout.Find("L11")!;
        Assert.Equal("Career Connect Application Aug 2026 - Present", heading.Text);
        var titleEnd = heading.Runs[0].X + _renderer.TextWidth(result.Layout, heading.Runs[0]) + _renderer.SpaceWidth(result.Layout, heading.Runs[0]);
        Assert.Equal(titleEnd, heading.Runs[1].X, 1);
        Assert.Equal(_layout.Lines.Select(l => (l.Id, l.Baseline)), result.Layout.Lines.Select(l => (l.Id, l.Baseline)));
    }

    [Fact]
    public async Task Swap_TurnsTheLinkLineIntoAPlainBullet_AndDropsItsLinkAndUnderline()
    {
        var result = await Apply(ProjectSwap());

        var bullet = result.Layout.Find("L13")!;
        Assert.True(bullet.Editable);
        Assert.Null(bullet.Runs[^1].Color);
        Assert.Empty(result.Layout.Links);

        var read = Assert.IsType<ResumeLayoutReadOutcome.Success>(new ResumeLayoutReader().Read(_renderer.Render(result.Layout))).Layout;
        Assert.Empty(read.Rules);
    }

    [Fact]
    public async Task Swap_KeepsRightAlignedDatesOnTheRightEdge()
    {
        var result = await Apply(InternSwap());

        Assert.Single(result.Swaps);
        var dates = result.Layout.Find("L06")!.Runs[1];
        Assert.Equal("Sept 2025 - Dec 2025", dates.Text);
        Assert.Equal(540, dates.X + _renderer.TextWidth(result.Layout, dates), 0.5);
        Assert.Equal("Acme | Springfield, IL", result.Layout.Find("L07")!.Text);
    }

    [Fact]
    public async Task Swap_IsRejected_WhenTheHeadingUsesWordsNotInTheExtraFacts()
    {
        var result = await Apply(ProjectSwap(title: "Career Connect Enterprise Platform"));

        Assert.Empty(result.Swaps);
        Assert.Contains("enterprise", Assert.Single(result.Rejected).Why);
        Assert.Equal(_layout.ToPlainText(), result.Layout.ToPlainText());
    }

    [Fact]
    public async Task Swap_IsRejected_WithoutDates()
    {
        var result = await Apply(ProjectSwap(dates: "Ongoing"));

        Assert.Contains("dates", Assert.Single(result.Rejected).Why);
    }

    [Fact]
    public async Task Swap_IsRejected_WhenTheYearIsntTheOneInTheExtraFacts()
    {
        var result = await Apply(ProjectSwap(dates: "Aug 2024 - Present"));

        Assert.Contains("dates", Assert.Single(result.Rejected).Why);
    }

    [Fact]
    public async Task Swap_IsRejected_WhenAJobWouldPutWorkHistoryOutOfOrder()
    {
        var future = InternSwap(dates: "May 2027 - June 2027", company: "Initech | Springfield, IL") with
        {
            HeaderLines = [new HeaderLineFields("L06", ["Web Developer", "May 2027 - June 2027"]), new HeaderLineFields("L07", ["Initech | Springfield, IL"])],
        };

        var result = await Apply(future);

        Assert.Contains("out of order", Assert.Single(result.Rejected).Why);
    }

    [Fact]
    public async Task Swap_IsRejected_WhenItDoesntFillEveryBullet()
    {
        var swap = ProjectSwap() with { Bullets = [new LineEdit("L12", "Built a full-stack job tracker with ASP.NET Core", "")] };

        var result = await Apply(swap);

        Assert.Contains("exactly", Assert.Single(result.Rejected).Why);
    }

    [Fact]
    public async Task Swap_IsRejected_WhenThereAreNoExtraFacts()
    {
        var result = await Apply(ProjectSwap(), context: _context with { ExtraFacts = null });

        Assert.Single(result.Rejected);
        Assert.Empty(result.Swaps);
    }

    [Fact]
    public async Task Swap_IsRejected_WhenABulletInventsANumber()
    {
        var result = await Apply(ProjectSwap(secondBullet: "Wrote 900 xUnit tests and deployed it on Railway with Docker"));

        Assert.Contains("900", Assert.Single(result.Rejected).Why);
    }

    [Fact]
    public async Task Swap_IsUndoneWhole_WhenOneOfItsBulletsNeverFits()
    {
        var tailorer = new FakeResumeLayoutTailorer { Fit = l => l.Text };

        var result = await Apply(ProjectSwap(secondBullet: string.Join(' ', Enumerable.Repeat("Wrote 257 xUnit tests and deployed it on Railway with Docker", 4))), tailorer);

        Assert.Empty(result.Swaps);
        Assert.Contains("Swap undone", Assert.Single(result.Rejected).Why);
        Assert.Equal(_layout.ToPlainText(), result.Layout.ToPlainText());
        Assert.Single(result.Layout.Links);
    }

    [Fact]
    public async Task Runner_SwapsOnTheFirstPass_ReportsItAsOneChange_AndTakesItBackWholeIfOneLineOverstates()
    {
        using var fixture = new TestDatabase();
        var userId = fixture.SeedUser("me@example.com");
        var application = fixture.SeedApplication(userId, status: ApplicationStatus.Preparing);
        var resume = fixture.SeedResume(userId, layout: _layout);
        var tracked = fixture.Db.Resumes.First(r => r.Id == resume.Id);
        tracked.ExtraFacts = TestResumes.SwapFacts;
        var run = new PrepRun { Id = Guid.NewGuid(), ApplicationId = application.Id, Status = PrepRunStatus.Running, TargetScore = 80, StartedAtUtc = DateTime.UtcNow };
        fixture.Db.PrepRuns.Add(run);
        fixture.Db.SaveChanges();

        var analyzer = new FakeResumeMatchAnalyzer();
        foreach (var score in new[] { 50, 70, 70 }) analyzer.ScoreSequence.Enqueue(score);
        var tailorer = new FakeResumeLayoutTailorer();
        tailorer.Proposals.Enqueue([]);
        tailorer.Proposals.Enqueue([new LineEdit("L04", "Supported production Linux systems through provisioning, imaging and lifecycle work", "r")]);
        tailorer.SwapProposals.Enqueue([ProjectSwap()]);
        var auditor = new FakeResumeClaimsAuditor();
        var runner = new ApplicationPrepRunner(fixture.Db, analyzer, tailorer, new ResumeEditGuard(_renderer, tailorer),
            auditor, new FakeResumeReviewer(), NullLogger<ApplicationPrepRunner>.Instance);

        auditor.Flag = [new UnsupportedClaim("L13", "deployment isn't in that project's facts")];
        await runner.ExecuteAsync(run.Id);

        Assert.Equal([true, false], tailorer.AllowSwapsByCall);
        var swappedChanges = auditor.LastChanges!.Where(c => c.Swap is not null).ToList();
        Assert.Equal(["L11", "L12", "L13"], swappedChanges.Select(c => c.LineId));
        Assert.All(swappedChanges, c => Assert.Equal("Drinks Around The World IOS Application → Career Connect Application", c.Swap));

        var completed = await fixture.Db.PrepRuns.AsNoTracking().FirstAsync(r => r.Id == run.Id);
        var saved = await fixture.Db.Applications.AsNoTracking().FirstAsync(a => a.Id == application.Id);
        Assert.DoesNotContain(completed.Changes, c => c.Swap is not null);
        Assert.Equal(_layout.Find("L11")!.Text, saved.TailoredResumeLayout!.Find("L11")!.Text);
        Assert.Single(saved.TailoredResumeLayout.Links);
    }
}
