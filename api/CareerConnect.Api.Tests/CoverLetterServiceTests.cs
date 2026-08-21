using CareerConnect.Api.Services;

namespace CareerConnect.Api.Tests;

public sealed class CoverLetterServiceTests : IDisposable
{
    private readonly TestDatabase _fixture = new();
    private readonly FakeCoverLetterGenerator _generator = new();
    private readonly CoverLetterService _service;
    private readonly Guid _userId;

    public CoverLetterServiceTests()
    {
        _userId = _fixture.SeedUser("me@example.com");
        _service = new CoverLetterService(_fixture.Db, _generator);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task GenerateAsync_ReturnsGeneratedLetter_UsingActiveResume()
    {
        var application = _fixture.SeedApplication(_userId);
        _fixture.SeedResume(_userId, "Stale", isActive: false);
        _fixture.SeedResume(_userId, "Current", isActive: true);
        _generator.Result = "Dear Hiring Team, I'm excited to apply...";

        var outcome = await _service.GenerateAsync(_userId, application.Id);

        var success = Assert.IsType<CoverLetterOutcome.Success>(outcome);
        Assert.Equal("Dear Hiring Team, I'm excited to apply...", success.Content);
    }

    [Fact]
    public async Task GenerateAsync_FailsWithoutJobDescription()
    {
        var application = _fixture.SeedApplication(_userId, jobDescription: null);
        _fixture.SeedResume(_userId);

        var outcome = await _service.GenerateAsync(_userId, application.Id);

        var failed = Assert.IsType<CoverLetterOutcome.Failed>(outcome);
        Assert.Equal(CoverLetterFailureReason.NoJobDescription, failed.Reason);
        Assert.Equal(0, _generator.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_FailsWithoutActiveResume()
    {
        var application = _fixture.SeedApplication(_userId);
        _fixture.SeedResume(_userId, isActive: false);

        var outcome = await _service.GenerateAsync(_userId, application.Id);

        var failed = Assert.IsType<CoverLetterOutcome.Failed>(outcome);
        Assert.Equal(CoverLetterFailureReason.NoActiveResume, failed.Reason);
        Assert.Equal(0, _generator.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_ReportsUnavailableWhenNoApiKeyConfigured()
    {
        var application = _fixture.SeedApplication(_userId);
        _fixture.SeedResume(_userId);
        _generator.IsConfigured = false;

        var outcome = await _service.GenerateAsync(_userId, application.Id);

        var failed = Assert.IsType<CoverLetterOutcome.Failed>(outcome);
        Assert.Equal(CoverLetterFailureReason.GeneratorUnavailable, failed.Reason);
    }

    [Fact]
    public async Task GenerateAsync_SurfacesGeneratorFailure()
    {
        var application = _fixture.SeedApplication(_userId);
        _fixture.SeedResume(_userId);
        _generator.ThrowOnGenerate = new CoverLetterGenerationException("Claude declined to write this cover letter.");

        var outcome = await _service.GenerateAsync(_userId, application.Id);

        var failed = Assert.IsType<CoverLetterOutcome.Failed>(outcome);
        Assert.Equal(CoverLetterFailureReason.GeneratorFailed, failed.Reason);
        Assert.Contains("declined", failed.Message);
    }

    [Fact]
    public async Task GenerateAsync_RejectsAnotherUsersApplication()
    {
        var otherUserId = _fixture.SeedUser("someone-else@example.com");
        var application = _fixture.SeedApplication(otherUserId);
        _fixture.SeedResume(_userId);

        var outcome = await _service.GenerateAsync(_userId, application.Id);

        var failed = Assert.IsType<CoverLetterOutcome.Failed>(outcome);
        Assert.Equal(CoverLetterFailureReason.ApplicationNotFound, failed.Reason);
    }
}
