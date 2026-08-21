using CareerConnect.Api.Services;

namespace CareerConnect.Api.Tests;

public sealed class InterviewPrepServiceTests : IDisposable
{
    private readonly TestDatabase _fixture = new();
    private readonly FakeInterviewPrepGenerator _generator = new();
    private readonly InterviewPrepService _service;
    private readonly Guid _userId;

    public InterviewPrepServiceTests()
    {
        _userId = _fixture.SeedUser("me@example.com");
        _service = new InterviewPrepService(_fixture.Db, _generator);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task GenerateAsync_ReturnsQuestionsAndTalkingPoints_UsingActiveResume()
    {
        var application = _fixture.SeedApplication(_userId);
        _fixture.SeedResume(_userId, "Stale", isActive: false);
        _fixture.SeedResume(_userId, "Current", isActive: true);

        var outcome = await _service.GenerateAsync(_userId, application.Id);

        var success = Assert.IsType<InterviewPrepOutcome.Success>(outcome);
        Assert.Single(success.Prep.Questions);
        Assert.Single(success.Prep.TalkingPoints);
    }

    [Fact]
    public async Task GenerateAsync_FailsWithoutJobDescription()
    {
        var application = _fixture.SeedApplication(_userId, jobDescription: null);
        _fixture.SeedResume(_userId);

        var outcome = await _service.GenerateAsync(_userId, application.Id);

        var failed = Assert.IsType<InterviewPrepOutcome.Failed>(outcome);
        Assert.Equal(InterviewPrepFailureReason.NoJobDescription, failed.Reason);
        Assert.Equal(0, _generator.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_FailsWithoutActiveResume()
    {
        var application = _fixture.SeedApplication(_userId);
        _fixture.SeedResume(_userId, isActive: false);

        var outcome = await _service.GenerateAsync(_userId, application.Id);

        var failed = Assert.IsType<InterviewPrepOutcome.Failed>(outcome);
        Assert.Equal(InterviewPrepFailureReason.NoActiveResume, failed.Reason);
        Assert.Equal(0, _generator.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_ReportsUnavailableWhenNoApiKeyConfigured()
    {
        var application = _fixture.SeedApplication(_userId);
        _fixture.SeedResume(_userId);
        _generator.IsConfigured = false;

        var outcome = await _service.GenerateAsync(_userId, application.Id);

        var failed = Assert.IsType<InterviewPrepOutcome.Failed>(outcome);
        Assert.Equal(InterviewPrepFailureReason.GeneratorUnavailable, failed.Reason);
    }

    [Fact]
    public async Task GenerateAsync_SurfacesGeneratorFailure()
    {
        var application = _fixture.SeedApplication(_userId);
        _fixture.SeedResume(_userId);
        _generator.ThrowOnGenerate = new InterviewPrepGenerationException("Claude couldn't generate interview prep.");

        var outcome = await _service.GenerateAsync(_userId, application.Id);

        var failed = Assert.IsType<InterviewPrepOutcome.Failed>(outcome);
        Assert.Equal(InterviewPrepFailureReason.GeneratorFailed, failed.Reason);
    }

    [Fact]
    public async Task GenerateAsync_RejectsAnotherUsersApplication()
    {
        var otherUserId = _fixture.SeedUser("someone-else@example.com");
        var application = _fixture.SeedApplication(otherUserId);
        _fixture.SeedResume(_userId);

        var outcome = await _service.GenerateAsync(_userId, application.Id);

        var failed = Assert.IsType<InterviewPrepOutcome.Failed>(outcome);
        Assert.Equal(InterviewPrepFailureReason.ApplicationNotFound, failed.Reason);
    }
}
