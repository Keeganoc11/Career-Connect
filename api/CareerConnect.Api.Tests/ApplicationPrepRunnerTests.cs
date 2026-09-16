using CareerConnect.Api.Domain;
using CareerConnect.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CareerConnect.Api.Tests;

public class ApplicationPrepRunnerTests : IDisposable
{
    private readonly TestDatabase _fixture = new();
    private readonly FakeResumeMatchAnalyzer _analyzer = new();
    private readonly FakeResumeLayoutTailorer _tailorer = new();
    private readonly FakeResumeClaimsAuditor _auditor = new();
    private readonly FakeResumeReviewer _reviewer = new();
    private readonly ApplicationPrepRunner _runner;
    private readonly Guid _userId;
    private readonly ResumeLayout _base = TestResumes.Layout();

    public ApplicationPrepRunnerTests()
    {
        _userId = _fixture.SeedUser("me@example.com");
        _runner = new ApplicationPrepRunner(
            _fixture.Db, _analyzer, _tailorer,
            new ResumeEditGuard(new ResumeRenderer(), _tailorer),
            _auditor, _reviewer,
            NullLogger<ApplicationPrepRunner>.Instance);
    }

    public void Dispose() => _fixture.Dispose();

    private PrepRun Seed(int targetScore = 80, bool withLayout = true)
    {
        var application = _fixture.SeedApplication(_userId, status: ApplicationStatus.Preparing);
        _fixture.SeedResume(_userId, layout: withLayout ? _base : null);
        return AddRun(application.Id, targetScore);
    }

    private PrepRun AddRun(Guid applicationId, int targetScore = 80, string? instructions = null)
    {
        var run = new PrepRun
        {
            Id = Guid.NewGuid(),
            ApplicationId = applicationId,
            Status = PrepRunStatus.Running,
            TargetScore = targetScore,
            Instructions = instructions,
            StartedAtUtc = DateTime.UtcNow,
        };
        _fixture.Db.PrepRuns.Add(run);
        _fixture.Db.SaveChanges();
        return run;
    }

    private void Scores(params int[] scores)
    {
        foreach (var score in scores)
        {
            _analyzer.ScoreSequence.Enqueue(score);
        }
    }

    private async Task<(PrepRun Run, Application Application)> RunAsync(PrepRun run)
    {
        await _runner.ExecuteAsync(run.Id);
        var completed = await _fixture.Db.PrepRuns.AsNoTracking().FirstAsync(r => r.Id == run.Id);
        var application = await _fixture.Db.Applications.AsNoTracking().FirstAsync(a => a.Id == run.ApplicationId);
        return (completed, application);
    }

    private string? TailoredBullet(Application application) =>
        application.TailoredResumeLayout?.Find(TestResumes.BulletLine)?.EditableText;

    [Fact]
    public async Task ExecuteAsync_SkipsTailoring_WhenBaselineAlreadyClearsTarget()
    {
        Scores(85);

        var (run, application) = await RunAsync(Seed());

        Assert.Equal(PrepRunStatus.Succeeded, run.Status);
        Assert.Equal(85, run.BaselineScore);
        Assert.Equal(85, run.FinalScore);
        Assert.Equal(0, run.Iterations);
        Assert.True(run.ReadyToApply);
        Assert.Equal(0, _tailorer.CallCount);
        // Still a file to download: the base resume, untouched.
        Assert.Equal(_base.ToPlainText(), application.TailoredResumeLayout!.ToPlainText());
        Assert.Empty(run.Changes);
    }

    [Fact]
    public async Task ExecuteAsync_TailorsUntilTargetIsCleared()
    {
        Scores(50, 70, 88);

        var (run, application) = await RunAsync(Seed());

        Assert.Equal(88, run.FinalScore);
        Assert.Equal(2, run.Iterations);
        Assert.True(run.ReadyToApply);
        Assert.Equal(FakeResumeLayoutTailorer.PassPhrases[1], TailoredBullet(application));
        Assert.Equal(application.TailoredResumeLayout!.ToPlainText(), application.TailoredResumeText);
    }

    [Fact]
    public async Task ExecuteAsync_StopsRewriting_WhenAPassDoesNotImprove()
    {
        Scores(50, 60, 55);

        var (run, application) = await RunAsync(Seed());

        Assert.Equal(60, run.FinalScore);
        Assert.Equal(2, run.Iterations);
        Assert.False(run.ReadyToApply);
        // The winner is pass 1, not the regressed pass 2.
        Assert.Equal(FakeResumeLayoutTailorer.PassPhrases[0], TailoredBullet(application));
    }

    [Fact]
    public async Task ExecuteAsync_KeepsATiedRewrite_SinceItIsStillReframedForThePosting()
    {
        Scores(50, 50);

        var (_, application) = await RunAsync(Seed());

        Assert.Equal(FakeResumeLayoutTailorer.PassPhrases[0], TailoredBullet(application));
        Assert.Equal(1, _tailorer.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_DiscardsARegressedRewrite_AndKeepsTheBaseResume()
    {
        Scores(50, 40);

        var (run, application) = await RunAsync(Seed());

        Assert.Equal(50, run.FinalScore);
        Assert.Equal(_base.ToPlainText(), application.TailoredResumeLayout!.ToPlainText());
        Assert.Empty(run.Changes);
        Assert.Equal(0, _auditor.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_StopsAfterThreePasses_WhenTargetIsNeverCleared()
    {
        Scores(10, 20, 30, 40);

        var (run, _) = await RunAsync(Seed());

        Assert.Equal(3, run.Iterations);
        Assert.Equal(40, run.FinalScore);
        Assert.False(run.ReadyToApply);
    }

    [Fact]
    public async Task ExecuteAsync_StopsWhenNoProposalSurvivesTheGuard()
    {
        Scores(50);
        _tailorer.Proposals.Enqueue([new LineEdit("L01", "Someone Else", "r")]);

        var (run, _) = await RunAsync(Seed());

        Assert.Equal(PrepRunStatus.Succeeded, run.Status);
        Assert.Equal(1, run.Iterations);
        Assert.Equal(1, _analyzer.CallCount);
        Assert.Contains(run.Steps, s => s.Label == "Nothing left to rewrite");
    }

    [Fact]
    public async Task ExecuteAsync_RecordsWhatChangedAndWhy()
    {
        Scores(50, 90);

        var (run, _) = await RunAsync(Seed());

        var change = Assert.Single(run.Changes);
        Assert.Equal(TestResumes.BulletLine, change.LineId);
        Assert.Equal(_base.Find(TestResumes.BulletLine)!.EditableText, change.Before);
        Assert.Equal(FakeResumeLayoutTailorer.PassPhrases[0], change.After);
        Assert.Equal("Reason 1", change.Reason);
    }

    [Fact]
    public async Task ExecuteAsync_PutsBackLinesTheClaimsCheckFlags_AndRescores()
    {
        Scores(50, 90, 52);
        _auditor.Flag = [new UnsupportedClaim(TestResumes.BulletLine, "adds production scale, never shown")];

        var (run, application) = await RunAsync(Seed());

        Assert.Equal(_base.Find(TestResumes.BulletLine)!.EditableText, TailoredBullet(application));
        Assert.Empty(run.Changes);
        Assert.Equal(52, run.FinalScore);
        Assert.False(run.ReadyToApply);
        Assert.Contains(run.Steps, s => s.Label.StartsWith("Put back 1 line"));
    }

    [Fact]
    public async Task ExecuteAsync_WritesTheRealityCheckAgainstTheFinalResume()
    {
        Scores(50, 90);

        var (run, application) = await RunAsync(Seed());

        Assert.NotNull(run.Review);
        Assert.Equal(FitVerdict.StrongFit, run.Review.Verdict);
        Assert.Equal(_reviewer.Result.RealityCheck, run.Review.RealityCheck);
        Assert.Equal(application.TailoredResumeText, _reviewer.LastResumeText);
    }

    [Fact]
    public async Task ExecuteAsync_ADealbreakerMeansNotReady_HoweverHighTheScore()
    {
        Scores(92);
        _reviewer.Result = _reviewer.Result with
        {
            Dealbreakers = [new Dealbreaker("5+ years of professional .NET", "About a year across an internship and projects.")],
        };

        var (run, _) = await RunAsync(Seed());

        Assert.False(run.ReadyToApply);
        Assert.Equal(FitVerdict.Stretch, run.Review!.Verdict);
    }

    [Fact]
    public async Task ExecuteAsync_RecordsEveryScoreAsMatchHistory()
    {
        Scores(50, 90);

        var (run, _) = await RunAsync(Seed());

        var results = await _fixture.Db.MatchResults.AsNoTracking()
            .Where(m => m.ApplicationId == run.ApplicationId)
            .OrderBy(m => m.Score)
            .ToListAsync();

        Assert.Equal(2, results.Count);
        Assert.False(results[0].UsedTailoredResume);
        Assert.True(results[1].UsedTailoredResume);
    }

    [Fact]
    public async Task ExecuteAsync_Fails_WhenTheActiveResumeHasNoLayout()
    {
        var (run, _) = await RunAsync(Seed(withLayout: false));

        Assert.Equal(PrepRunStatus.Failed, run.Status);
        Assert.Contains("PDF", run.ErrorMessage);
        Assert.Equal(0, _analyzer.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_Fails_WhenThereIsNoActiveResume()
    {
        var application = _fixture.SeedApplication(_userId, status: ApplicationStatus.Preparing);
        var run = new PrepRun
        {
            Id = Guid.NewGuid(),
            ApplicationId = application.Id,
            Status = PrepRunStatus.Running,
            TargetScore = 80,
            StartedAtUtc = DateTime.UtcNow,
        };
        _fixture.Db.PrepRuns.Add(run);
        _fixture.Db.SaveChanges();

        var (completed, _) = await RunAsync(run);

        Assert.Equal(PrepRunStatus.Failed, completed.Status);
        Assert.False(completed.ReadyToApply);
        Assert.Contains("active", completed.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_Fails_WithTheModelsMessage_WhenAStepThrows()
    {
        Scores(50);
        _tailorer.ThrowOnTailor = new ResumeTailoringException("Upstream is down.");

        var (run, _) = await RunAsync(Seed());

        Assert.Equal(PrepRunStatus.Failed, run.Status);
        Assert.Equal("Upstream is down.", run.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_ARequestedChangeRewritesEvenWhenTheScoreAlreadyClearsTheBar()
    {
        Scores(85, 86);
        var application = _fixture.SeedApplication(_userId, status: ApplicationStatus.Preparing);
        _fixture.SeedResume(_userId, layout: _base);

        var (run, _) = await RunAsync(AddRun(application.Id, instructions: "Lean more backend"));

        Assert.Equal(1, run.Iterations);
        Assert.Equal("Lean more backend", _tailorer.LastInstructions);
        Assert.Equal(86, run.FinalScore);
    }

    [Fact]
    public async Task ExecuteAsync_ARequestedChangeBuildsOnTheCurrentTailoredVersion_AndKeepsItsReasons()
    {
        var application = _fixture.SeedApplication(_userId, status: ApplicationStatus.Preparing);
        _fixture.SeedResume(_userId, layout: _base);

        Scores(50, 90);
        await RunAsync(AddRun(application.Id));

        // Second pass asks for something else, on a different line.
        Scores(50, 90, 91);
        _tailorer.Proposals.Enqueue([new LineEdit(TestResumes.SecondBulletLine, "Wrote xUnit tests and cut regressions in half", "Leads with xUnit.")]);
        var (run, updated) = await RunAsync(AddRun(application.Id, instructions: "Mention xUnit first"));

        // Started from the tailored version, not the base: the first rewrite survives.
        Assert.Equal(FakeResumeLayoutTailorer.PassPhrases[0], _tailorer.LastCurrent!.Find(TestResumes.BulletLine)!.EditableText);
        Assert.Equal(FakeResumeLayoutTailorer.PassPhrases[0], TailoredBullet(updated));
        Assert.Equal(["Reason 1", "Leads with xUnit."], run.Changes.Select(c => c.Reason));
    }

    [Fact]
    public async Task ExecuteAsync_KeepsARequestedChange_EvenWhenItScoresLower()
    {
        Scores(85, 80);
        var application = _fixture.SeedApplication(_userId, status: ApplicationStatus.Preparing);
        _fixture.SeedResume(_userId, layout: _base);

        var (run, updated) = await RunAsync(AddRun(application.Id, instructions: "Shorter bullets"));

        Assert.Equal(FakeResumeLayoutTailorer.PassPhrases[0], TailoredBullet(updated));
        Assert.Equal(80, run.FinalScore);
        Assert.Contains(run.Steps, s => s.Label == "Kept the change you asked for");
    }

    [Fact]
    public async Task ExecuteAsync_DoesNothing_WhenTheRunAlreadyFinished()
    {
        var run = Seed();
        run.Status = PrepRunStatus.Succeeded;
        _fixture.Db.SaveChanges();

        await _runner.ExecuteAsync(run.Id);

        Assert.Equal(0, _analyzer.CallCount);
    }
}
