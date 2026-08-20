using CareerConnect.Api.Services;

namespace CareerConnect.Api.Tests;

public sealed class ResumeTailorServiceTests : IDisposable
{
    private readonly TestDatabase _fixture = new();
    private readonly FakeResumeTailorer _tailorer = new();
    private readonly ResumeTailorService _service;
    private readonly Guid _userId;

    public ResumeTailorServiceTests()
    {
        _userId = _fixture.SeedUser("me@example.com");
        _service = new ResumeTailorService(_fixture.Db, _tailorer);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task TailorAsync_ReturnsTailoredContent_OnSuccess()
    {
        var application = _fixture.SeedApplication(_userId);

        var outcome = await _service.TailorAsync(_userId, application.Id, "my resume text");

        var success = Assert.IsType<TailorOutcome.Success>(outcome);
        Assert.Equal("Tailored resume text.", success.Content);
        Assert.Equal("my resume text", _tailorer.LastResumeText);
        Assert.Equal(application.JobDescriptionText, _tailorer.LastJobDescriptionText);
    }

    [Fact]
    public async Task TailorAsync_FailsAsApplicationNotFound_ForUnknownOrOtherUsersApplication()
    {
        var outcome = await _service.TailorAsync(_userId, Guid.NewGuid(), "my resume text");

        var failed = Assert.IsType<TailorOutcome.Failed>(outcome);
        Assert.Equal(TailorFailureReason.ApplicationNotFound, failed.Reason);
        Assert.Equal(0, _tailorer.CallCount);
    }

    [Fact]
    public async Task TailorAsync_FailsAsNoJobDescription_WhenApplicationHasNone()
    {
        var application = _fixture.SeedApplication(_userId, jobDescription: null);

        var outcome = await _service.TailorAsync(_userId, application.Id, "my resume text");

        var failed = Assert.IsType<TailorOutcome.Failed>(outcome);
        Assert.Equal(TailorFailureReason.NoJobDescription, failed.Reason);
        Assert.Equal(0, _tailorer.CallCount);
    }

    [Fact]
    public async Task TailorAsync_FailsAsTailorerUnavailable_WhenNotConfigured()
    {
        var application = _fixture.SeedApplication(_userId);
        _tailorer.IsConfigured = false;

        var outcome = await _service.TailorAsync(_userId, application.Id, "my resume text");

        var failed = Assert.IsType<TailorOutcome.Failed>(outcome);
        Assert.Equal(TailorFailureReason.TailorerUnavailable, failed.Reason);
        Assert.Equal(0, _tailorer.CallCount);
    }

    [Fact]
    public async Task TailorAsync_FailsAsTailorerFailed_WhenTailorerThrows()
    {
        var application = _fixture.SeedApplication(_userId);
        _tailorer.ThrowOnTailor = new ResumeTailorException("The tailoring request to Claude failed.");

        var outcome = await _service.TailorAsync(_userId, application.Id, "my resume text");

        var failed = Assert.IsType<TailorOutcome.Failed>(outcome);
        Assert.Equal(TailorFailureReason.TailorerFailed, failed.Reason);
    }
}
