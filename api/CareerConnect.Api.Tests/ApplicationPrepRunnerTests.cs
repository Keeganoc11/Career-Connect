using CareerConnect.Api.Domain;
using CareerConnect.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CareerConnect.Api.Tests;

public class ApplicationPrepRunnerTests : IDisposable
{
    private readonly TestDatabase _fixture = new();
    private readonly FakeResumeMatchAnalyzer _analyzer = new();
    private readonly FakeResumeTailorer _tailorer = new();
    private readonly FakeCoverLetterGenerator _coverLetters = new();
    private readonly ApplicationPrepRunner _runner;
    private readonly Guid _userId;

    public ApplicationPrepRunnerTests()
    {
        _userId = _fixture.SeedUser("me@example.com");
        _runner = new ApplicationPrepRunner(
            _fixture.Db, _analyzer, _tailorer, _coverLetters,
            NullLogger<ApplicationPrepRunner>.Instance);
    }

    public void Dispose() => _fixture.Dispose();

    private PrepRun SeedRun(Application application, int targetScore = 80)
    {
        var run = new PrepRun
        {
            Id = Guid.NewGuid(),
            ApplicationId = application.Id,
            Status = PrepRunStatus.Running,
            TargetScore = targetScore,
            StartedAtUtc = DateTime.UtcNow,
        };
        _fixture.Db.PrepRuns.Add(run);
        _fixture.Db.SaveChanges();
        return run;
    }

    private Task<PrepRun> ReloadAsync(Guid runId) =>
        _fixture.Db.PrepRuns.AsNoTracking().FirstAsync(r => r.Id == runId);

    [Fact]
    public async Task ExecuteAsync_SkipsTailoring_WhenBaselineAlreadyClearsTarget()
    {
        var application = _fixture.SeedApplication(_userId, status: ApplicationStatus.Preparing);
        _fixture.SeedResume(_userId);
        _analyzer.ScoreSequence.Enqueue(85);
        var run = SeedRun(application);

        await _runner.ExecuteAsync(run.Id);

        var completed = await ReloadAsync(run.Id);
        Assert.Equal(PrepRunStatus.Succeeded, completed.Status);
        Assert.Equal(85, completed.BaselineScore);
        Assert.Equal(85, completed.FinalScore);
        Assert.Equal(0, completed.Iterations);
        Assert.True(completed.ReadyToApply);
        Assert.Equal(0, _tailorer.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_TailorsUntilTargetIsCleared()
    {
        var application = _fixture.SeedApplication(_userId, status: ApplicationStatus.Preparing);
        _fixture.SeedResume(_userId);
        _analyzer.ScoreSequence.Enqueue(50); // baseline
        _analyzer.ScoreSequence.Enqueue(70); // after first rewrite — still short
        _analyzer.ScoreSequence.Enqueue(88); // after second — clears 80
        var run = SeedRun(application);

        await _runner.ExecuteAsync(run.Id);

        var completed = await ReloadAsync(run.Id);
        Assert.Equal(PrepRunStatus.Succeeded, completed.Status);
        Assert.Equal(50, completed.BaselineScore);
        Assert.Equal(88, completed.FinalScore);
        Assert.Equal(2, completed.Iterations);
        Assert.True(completed.ReadyToApply);
        Assert.Equal(2, _tailorer.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_StopsRewriting_WhenAPassDoesNotImprove()
    {
        var application = _fixture.SeedApplication(_userId, status: ApplicationStatus.Preparing);
        _fixture.SeedResume(_userId);
        _analyzer.ScoreSequence.Enqueue(50); // baseline
        _analyzer.ScoreSequence.Enqueue(60); // better, keep going
        _analyzer.ScoreSequence.Enqueue(55); // worse — give up rather than burn a third pass
        var run = SeedRun(application);

        await _runner.ExecuteAsync(run.Id);

        var completed = await ReloadAsync(run.Id);
        Assert.Equal(PrepRunStatus.Succeeded, completed.Status);
        Assert.Equal(60, completed.FinalScore);
        Assert.Equal(2, completed.Iterations);
        Assert.False(completed.ReadyToApply);
        Assert.Equal(2, _tailorer.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_KeepsTheBestRewrite_NotTheLastOne()
    {
        var application = _fixture.SeedApplication(_userId, status: ApplicationStatus.Preparing);
        _fixture.SeedResume(_userId);
        _analyzer.ScoreSequence.Enqueue(50);
        _analyzer.ScoreSequence.Enqueue(60); // pass 1 — the winner
        _analyzer.ScoreSequence.Enqueue(55); // pass 2 — regressed, must not be saved
        var run = SeedRun(application);

        await _runner.ExecuteAsync(run.Id);

        var saved = await _fixture.Db.Applications.AsNoTracking().FirstAsync(a => a.Id == application.Id);
        Assert.Equal(_tailorer.Result, saved.TailoredResumeText);
    }

    [Fact]
    public async Task ExecuteAsync_KeepsATiedRewrite_SinceItIsStillReframedForThePosting()
    {
        // Scoring the same doesn't mean the rewrite was worthless — it's still
        // written in the posting's language. Only a regression is discarded.
        var application = _fixture.SeedApplication(_userId, status: ApplicationStatus.Preparing);
        _fixture.SeedResume(_userId);
        _analyzer.ScoreSequence.Enqueue(50);
        _analyzer.ScoreSequence.Enqueue(50);
        var run = SeedRun(application);

        await _runner.ExecuteAsync(run.Id);

        var saved = await _fixture.Db.Applications.AsNoTracking().FirstAsync(a => a.Id == application.Id);
        Assert.Equal(_tailorer.Result, saved.TailoredResumeText);
        Assert.Equal(1, _tailorer.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_DiscardsARegressedRewrite_AndFallsBackToTheBaseResume()
    {
        var application = _fixture.SeedApplication(_userId, status: ApplicationStatus.Preparing);
        _fixture.SeedResume(_userId);
        _analyzer.ScoreSequence.Enqueue(50);
        _analyzer.ScoreSequence.Enqueue(40); // strictly worse than the untouched resume
        var run = SeedRun(application);

        await _runner.ExecuteAsync(run.Id);

        var saved = await _fixture.Db.Applications.AsNoTracking().FirstAsync(a => a.Id == application.Id);
        Assert.Null(saved.TailoredResumeText);

        var completed = await ReloadAsync(run.Id);
        Assert.Equal(50, completed.FinalScore);
    }

    [Fact]
    public async Task ExecuteAsync_StopsAfterThreePasses_WhenTargetIsNeverCleared()
    {
        var application = _fixture.SeedApplication(_userId, status: ApplicationStatus.Preparing);
        _fixture.SeedResume(_userId);
        foreach (var score in new[] { 10, 20, 30, 40 })
        {
            _analyzer.ScoreSequence.Enqueue(score);
        }
        var run = SeedRun(application);

        await _runner.ExecuteAsync(run.Id);

        var completed = await ReloadAsync(run.Id);
        Assert.Equal(3, completed.Iterations);
        Assert.Equal(40, completed.FinalScore);
        Assert.False(completed.ReadyToApply);
    }

    [Fact]
    public async Task ExecuteAsync_WritesCoverLetterAgainstTheTailoredResume()
    {
        var application = _fixture.SeedApplication(_userId, status: ApplicationStatus.Preparing);
        _fixture.SeedResume(_userId);
        _analyzer.ScoreSequence.Enqueue(50);
        _analyzer.ScoreSequence.Enqueue(90);
        var run = SeedRun(application);

        await _runner.ExecuteAsync(run.Id);

        var saved = await _fixture.Db.Applications.AsNoTracking().FirstAsync(a => a.Id == application.Id);
        Assert.Equal(_coverLetters.Result, saved.CoverLetterText);
        Assert.Equal(1, _coverLetters.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_RecordsEveryScoreAsMatchHistory()
    {
        var application = _fixture.SeedApplication(_userId, status: ApplicationStatus.Preparing);
        _fixture.SeedResume(_userId);
        _analyzer.ScoreSequence.Enqueue(50);
        _analyzer.ScoreSequence.Enqueue(90);
        var run = SeedRun(application);

        await _runner.ExecuteAsync(run.Id);

        var results = await _fixture.Db.MatchResults
            .AsNoTracking()
            .Where(m => m.ApplicationId == application.Id)
            .OrderBy(m => m.Score)
            .ToListAsync();

        Assert.Equal(2, results.Count);
        Assert.False(results[0].UsedTailoredResume);
        Assert.True(results[1].UsedTailoredResume);
    }

    [Fact]
    public async Task ExecuteAsync_Fails_WhenThereIsNoActiveResume()
    {
        var application = _fixture.SeedApplication(_userId, status: ApplicationStatus.Preparing);
        var run = SeedRun(application);

        await _runner.ExecuteAsync(run.Id);

        var completed = await ReloadAsync(run.Id);
        Assert.Equal(PrepRunStatus.Failed, completed.Status);
        Assert.False(completed.ReadyToApply);
        Assert.Contains("active", completed.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_Fails_WhenTheModelCallThrows()
    {
        var application = _fixture.SeedApplication(_userId, status: ApplicationStatus.Preparing);
        _fixture.SeedResume(_userId);
        _analyzer.ThrowOnAnalyze = new MatchAnalysisException("Upstream is down.");
        var run = SeedRun(application);

        await _runner.ExecuteAsync(run.Id);

        var completed = await ReloadAsync(run.Id);
        Assert.Equal(PrepRunStatus.Failed, completed.Status);
        Assert.Equal("Upstream is down.", completed.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNothing_WhenTheRunAlreadyFinished()
    {
        var application = _fixture.SeedApplication(_userId, status: ApplicationStatus.Preparing);
        _fixture.SeedResume(_userId);
        var run = SeedRun(application);
        run.Status = PrepRunStatus.Succeeded;
        _fixture.Db.SaveChanges();

        await _runner.ExecuteAsync(run.Id);

        Assert.Equal(0, _analyzer.CallCount);
    }
}
