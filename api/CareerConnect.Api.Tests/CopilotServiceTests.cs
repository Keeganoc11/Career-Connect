using CareerConnect.Api.Domain;
using CareerConnect.Api.Services;

namespace CareerConnect.Api.Tests;

public sealed class CopilotServiceTests : IDisposable
{
    private readonly TestDatabase _fixture = new();
    private readonly FakeCopilotAnalyzer _analyzer = new();
    private readonly CopilotService _service;
    private readonly Guid _userId;

    public CopilotServiceTests()
    {
        _userId = _fixture.SeedUser("me@example.com");
        _service = new CopilotService(_fixture.Db, _analyzer);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task AnalyzeAsync_ResolvesApplicationIndexToRealId_OnSuccess()
    {
        var application = _fixture.SeedApplication(_userId, "Acme");

        var outcome = await _service.AnalyzeAsync(_userId);

        var success = Assert.IsType<CopilotOutcome.Success>(outcome);
        var action = Assert.Single(success.Insights.Actions);
        Assert.Equal(application.Id, action.ApplicationId);
    }

    [Fact]
    public async Task AnalyzeAsync_ExcludesRejectedAndWithdrawnFromSnapshot()
    {
        _fixture.SeedApplication(_userId, "Live One", status: ApplicationStatus.Applied);
        _fixture.SeedApplication(_userId, "Dead One", status: ApplicationStatus.Rejected);
        _fixture.SeedApplication(_userId, "Gone One", status: ApplicationStatus.Withdrawn);

        await _service.AnalyzeAsync(_userId);

        var snapshot = _analyzer.LastSnapshot;
        Assert.NotNull(snapshot);
        Assert.Single(snapshot!.Applications);
        Assert.Equal("Live One", snapshot.Applications[0].CompanyName);
    }

    [Fact]
    public async Task AnalyzeAsync_IncludesLatestMatchScoreInSnapshot()
    {
        var application = _fixture.SeedApplication(_userId);
        var resume = _fixture.SeedResume(_userId);
        _fixture.Db.MatchResults.Add(new MatchResult
        {
            Id = Guid.NewGuid(),
            ApplicationId = application.Id,
            ResumeId = resume.Id,
            Score = 42,
            Summary = "eh",
            MatchedKeywords = [],
            MissingKeywords = [],
            Suggestions = [],
            ModelId = "claude-sonnet-5",
            CreatedAtUtc = DateTime.UtcNow,
        });
        _fixture.Db.SaveChanges();

        await _service.AnalyzeAsync(_userId);

        Assert.Equal(42, _analyzer.LastSnapshot!.Applications[0].LatestMatchScore);
    }

    [Fact]
    public async Task AnalyzeAsync_LeavesApplicationIdNull_WhenActionHasNoIndex()
    {
        _fixture.SeedApplication(_userId);
        _analyzer.Result = new CopilotInsights(
            "Add a resume to unlock scoring.",
            [new CopilotAction("Add a resume", "Nothing can be scored yet.", "high", null)]);

        var outcome = await _service.AnalyzeAsync(_userId);

        var success = Assert.IsType<CopilotOutcome.Success>(outcome);
        Assert.Null(Assert.Single(success.Insights.Actions).ApplicationId);
    }

    [Fact]
    public async Task AnalyzeAsync_FailsAsAnalyzerUnavailable_WhenNotConfigured()
    {
        _analyzer.IsConfigured = false;

        var outcome = await _service.AnalyzeAsync(_userId);

        var failed = Assert.IsType<CopilotOutcome.Failed>(outcome);
        Assert.Equal(CopilotFailureReason.AnalyzerUnavailable, failed.Reason);
        Assert.Null(_analyzer.LastSnapshot);
    }

    [Fact]
    public async Task AnalyzeAsync_FailsAsAnalyzerFailed_WhenAnalyzerThrows()
    {
        _analyzer.ThrowOnAnalyze = new CopilotAnalysisException("The insights request to Claude failed.");

        var outcome = await _service.AnalyzeAsync(_userId);

        var failed = Assert.IsType<CopilotOutcome.Failed>(outcome);
        Assert.Equal(CopilotFailureReason.AnalyzerFailed, failed.Reason);
    }
}
