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
    public async Task GenerateAsync_StoresThePrepSoItSurvivesClosingTheModal()
    {
        var application = _fixture.SeedApplication(_userId);
        _fixture.SeedResume(_userId);

        await _service.GenerateAsync(_userId, application.Id);

        var stored = await _service.GetStoredAsync(_userId, application.Id);
        Assert.NotNull(stored);
        Assert.Single(stored.Questions);
        Assert.NotNull(_fixture.Db.Applications.Single().InterviewPrepGeneratedAtUtc);
    }

    [Fact]
    public async Task GenerateAsync_ServesTheStoredCopyInsteadOfPayingForItTwice()
    {
        var application = _fixture.SeedApplication(_userId);
        _fixture.SeedResume(_userId);

        await _service.GenerateAsync(_userId, application.Id);
        var outcome = await _service.GenerateAsync(_userId, application.Id);

        Assert.IsType<InterviewPrepOutcome.Success>(outcome);
        Assert.Equal(1, _generator.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_RunsAgainWhenTheCallerAsksForAFreshPass()
    {
        var application = _fixture.SeedApplication(_userId);
        _fixture.SeedResume(_userId);

        await _service.GenerateAsync(_userId, application.Id);
        await _service.GenerateAsync(_userId, application.Id, regenerate: true);

        Assert.Equal(2, _generator.CallCount);
    }

    [Fact]
    public async Task GetStoredAsync_ReturnsNothingBeforeAnyPrepHasBeenGenerated()
    {
        var application = _fixture.SeedApplication(_userId);

        Assert.Null(await _service.GetStoredAsync(_userId, application.Id));
        Assert.Equal(0, _generator.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_RegeneratesRatherThanFailing_WhenTheStoredPrepIsUnreadable()
    {
        var application = _fixture.SeedApplication(_userId);
        _fixture.SeedResume(_userId);
        application.InterviewPrepJson = "{ this is not json";
        _fixture.Db.SaveChanges();

        var outcome = await _service.GenerateAsync(_userId, application.Id);

        Assert.IsType<InterviewPrepOutcome.Success>(outcome);
        Assert.Equal(1, _generator.CallCount);
    }

    [Fact]
    public async Task GetStoredAsync_HidesPrepBelongingToSomeoneElse()
    {
        var otherUserId = _fixture.SeedUser("someone-else@example.com");
        var application = _fixture.SeedApplication(otherUserId);
        _fixture.SeedResume(otherUserId);
        await _service.GenerateAsync(otherUserId, application.Id);

        Assert.Null(await _service.GetStoredAsync(_userId, application.Id));
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
