using CareerConnect.Api.Contracts;
using CareerConnect.Api.Domain;
using CareerConnect.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace CareerConnect.Api.Tests;

public sealed class MatchScoringServiceTests : IDisposable
{
    private readonly TestDatabase _fixture = new();
    private readonly FakeResumeMatchAnalyzer _analyzer = new();
    private readonly MatchScoringService _service;
    private readonly Guid _userId;

    public MatchScoringServiceTests()
    {
        _userId = _fixture.SeedUser("me@example.com");
        _service = new MatchScoringService(_fixture.Db, _analyzer);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task ScoreAsync_StoresResultAndPassesActiveResume()
    {
        var application = _fixture.SeedApplication(_userId);
        _fixture.SeedResume(_userId, "Stale", isActive: false);
        var active = _fixture.SeedResume(_userId, "Current", isActive: true);

        var outcome = await _service.ScoreAsync(_userId, application.Id);

        var success = Assert.IsType<ScoreOutcome.Success>(outcome);
        Assert.Equal(72, success.Result.Score);
        Assert.Equal("Current", success.Result.ResumeLabel);
        Assert.Equal(active.Content, _analyzer.LastResumeText);
        Assert.Equal(application.JobDescriptionText, _analyzer.LastJobDescriptionText);
        Assert.Single(await _fixture.Db.MatchResults.ToListAsync());
    }

    [Fact]
    public async Task ScoreAsync_PersistsKeywordListsThroughJsonConversion()
    {
        var application = _fixture.SeedApplication(_userId);
        _fixture.SeedResume(_userId);

        await _service.ScoreAsync(_userId, application.Id);

        // Read back through a fresh query so the value converter round-trips.
        var stored = await _fixture.Db.MatchResults.AsNoTracking().SingleAsync();
        Assert.Equal(["ASP.NET Core", "Entity Framework"], stored.MatchedKeywords);
        Assert.Equal(["Azure", "Kubernetes"], stored.MissingKeywords);
        var suggestion = Assert.Single(stored.Suggestions);
        Assert.Equal("Experience — Data Center Ops", suggestion.Section);
        Assert.Contains("SQL", suggestion.SuggestedText);
    }

    [Fact]
    public async Task ScoreAsync_ReadsPreUpgradeStringSuggestionsWithoutError()
    {
        // Scores written before Suggestions became structured stored a plain
        // string array. Confirm that legacy JSON still loads instead of
        // breaking every previously-scored application.
        var application = _fixture.SeedApplication(_userId);
        _fixture.SeedResume(_userId);

        var legacyRow = new MatchResult
        {
            Id = Guid.NewGuid(),
            ApplicationId = application.Id,
            ResumeId = (await _fixture.Db.Resumes.FirstAsync()).Id,
            Score = 40,
            Summary = "Legacy row from before structured suggestions.",
            ModelId = "claude-opus-5",
            CreatedAtUtc = DateTime.UtcNow,
        };
        await _fixture.Db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO MatchResults
                (Id, ApplicationId, ResumeId, Score, Summary, MatchedKeywords, MissingKeywords, Suggestions, ModelId, CreatedAtUtc)
            VALUES
                ({legacyRow.Id}, {legacyRow.ApplicationId}, {legacyRow.ResumeId}, {legacyRow.Score}, {legacyRow.Summary},
                 '[]', '[]', '["Add a project to the Projects section."]', {legacyRow.ModelId}, {legacyRow.CreatedAtUtc})
            """);

        var latest = await _service.GetLatestAsync(_userId, application.Id);

        Assert.NotNull(latest);
        var suggestion = Assert.Single(latest.Suggestions);
        Assert.Equal("General", suggestion.Section);
        Assert.Equal("Add a project to the Projects section.", suggestion.Guidance);
        Assert.Equal("", suggestion.SuggestedText);
    }

    [Fact]
    public async Task ScoreAsync_FailsWithoutJobDescription()
    {
        var application = _fixture.SeedApplication(_userId, jobDescription: null);
        _fixture.SeedResume(_userId);

        var outcome = await _service.ScoreAsync(_userId, application.Id);

        var failed = Assert.IsType<ScoreOutcome.Failed>(outcome);
        Assert.Equal(MatchFailureReason.NoJobDescription, failed.Reason);
        Assert.Equal(0, _analyzer.CallCount);
    }

    [Fact]
    public async Task ScoreAsync_FailsWithoutActiveResume()
    {
        var application = _fixture.SeedApplication(_userId);
        _fixture.SeedResume(_userId, isActive: false);

        var outcome = await _service.ScoreAsync(_userId, application.Id);

        var failed = Assert.IsType<ScoreOutcome.Failed>(outcome);
        Assert.Equal(MatchFailureReason.NoActiveResume, failed.Reason);
        Assert.Equal(0, _analyzer.CallCount);
    }

    [Fact]
    public async Task ScoreAsync_ReportsUnavailableWhenNoApiKeyConfigured()
    {
        var application = _fixture.SeedApplication(_userId);
        _fixture.SeedResume(_userId);
        _analyzer.IsConfigured = false;

        var outcome = await _service.ScoreAsync(_userId, application.Id);

        var failed = Assert.IsType<ScoreOutcome.Failed>(outcome);
        Assert.Equal(MatchFailureReason.AnalyzerUnavailable, failed.Reason);
    }

    [Fact]
    public async Task ScoreAsync_SurfacesAnalyzerFailureWithoutStoringAResult()
    {
        var application = _fixture.SeedApplication(_userId);
        _fixture.SeedResume(_userId);
        _analyzer.ThrowOnAnalyze = new MatchAnalysisException("Claude declined to analyze this content.");

        var outcome = await _service.ScoreAsync(_userId, application.Id);

        var failed = Assert.IsType<ScoreOutcome.Failed>(outcome);
        Assert.Equal(MatchFailureReason.AnalyzerFailed, failed.Reason);
        Assert.Contains("declined", failed.Message);
        Assert.Empty(await _fixture.Db.MatchResults.ToListAsync());
    }

    [Fact]
    public async Task ScoreAsync_RejectsAnotherUsersApplication()
    {
        var otherUserId = _fixture.SeedUser("someone-else@example.com");
        var application = _fixture.SeedApplication(otherUserId);
        _fixture.SeedResume(_userId);

        var outcome = await _service.ScoreAsync(_userId, application.Id);

        var failed = Assert.IsType<ScoreOutcome.Failed>(outcome);
        Assert.Equal(MatchFailureReason.ApplicationNotFound, failed.Reason);
    }

    [Fact]
    public async Task GetLatestAsync_ReturnsMostRecentScoreAndKeepsHistory()
    {
        var application = _fixture.SeedApplication(_userId);
        _fixture.SeedResume(_userId);

        await _service.ScoreAsync(_userId, application.Id);
        _analyzer.Result = _analyzer.Result with { Score = 91, Summary = "Much stronger after edits." };
        await _service.ScoreAsync(_userId, application.Id);

        var latest = await _service.GetLatestAsync(_userId, application.Id);

        Assert.NotNull(latest);
        Assert.Equal(91, latest.Score);
        // Re-scoring appends rather than overwriting, so progress stays visible.
        Assert.Equal(2, await _fixture.Db.MatchResults.CountAsync());
    }

    [Fact]
    public async Task GetLatestForAllAsync_ReturnsOneEntryPerApplication()
    {
        var first = _fixture.SeedApplication(_userId, "Acme");
        var second = _fixture.SeedApplication(_userId, "Globex");
        _fixture.SeedResume(_userId);

        await _service.ScoreAsync(_userId, first.Id);
        await _service.ScoreAsync(_userId, second.Id);
        await _service.ScoreAsync(_userId, first.Id);

        var all = await _service.GetLatestForAllAsync(_userId);

        Assert.Equal(2, all.Count);
        Assert.True(all.ContainsKey(first.Id));
        Assert.True(all.ContainsKey(second.Id));
    }
}
