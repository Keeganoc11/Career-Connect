using CareerConnect.Api.Contracts;
using CareerConnect.Api.Domain;
using CareerConnect.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CareerConnect.Api.Tests;

public sealed class JobCaptureServiceTests : IDisposable
{
    private readonly TestDatabase _fixture = new();
    private readonly FakeJobPostingIdentifier _identifier = new();
    private readonly FakeJobPostingFetcher _fetcher = new();
    private readonly FakeJobPostingExtractor _extractor = new();
    private readonly PrepRunQueue _queue = new();
    private readonly JobCaptureService _service;
    private readonly Guid _userId;

    private static readonly string Posting =
        "Stripe is hiring a Software Engineer, Payments. " + string.Join(' ', Enumerable.Repeat("You will build reliable APIs in a distributed system.", 6));

    public JobCaptureServiceTests()
    {
        _userId = _fixture.SeedUser("me@example.com");
        _service = new JobCaptureService(
            _fixture.Db,
            _identifier,
            new JobPostingIngestService(_fetcher, _extractor),
            new ApplicationService(_fixture.Db, new FakeInterviewCalendarSync()),
            new PrepRunService(_fixture.Db, _queue, new FakeResumeMatchAnalyzer(), new ConfigurationBuilder().Build()));
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task CaptureAsync_CreatesAPreparingApplication_WithThePastedTextVerbatim_AndStartsTailoring()
    {
        _fixture.SeedResume(_userId, layout: TestResumes.Layout());

        var outcome = await _service.CaptureAsync(_userId, new CaptureJobRequest
        {
            JobDescriptionText = $"  {Posting}  ",
            LocalDate = new DateOnly(2026, 9, 16),
        });

        var captured = Assert.IsType<JobCaptureOutcome.Captured>(outcome);
        Assert.Equal("Stripe", captured.Application.CompanyName);
        Assert.Equal("Software Engineer, Payments", captured.Application.RoleTitle);
        Assert.Equal(ApplicationStatus.Preparing, captured.Application.Status);
        Assert.Equal(new DateOnly(2026, 9, 16), captured.Application.DateApplied);
        Assert.Equal(Posting, captured.Application.JobDescriptionText);
        Assert.NotNull(captured.Run);
        Assert.Null(captured.PrepMessage);
    }

    [Fact]
    public async Task CaptureAsync_StillSavesTheApplication_WhenTailoringCantStart_AndSaysWhy()
    {
        _fixture.SeedResume(_userId); // pasted text, no PDF layout

        var outcome = await _service.CaptureAsync(_userId, new CaptureJobRequest { JobDescriptionText = Posting });

        var captured = Assert.IsType<JobCaptureOutcome.Captured>(outcome);
        Assert.Null(captured.Run);
        Assert.Contains("PDF", captured.PrepMessage);
        Assert.Equal(1, await _fixture.Db.Applications.CountAsync());
    }

    [Fact]
    public async Task CaptureAsync_RejectsTextTooShortToBeADescription()
    {
        var outcome = await _service.CaptureAsync(_userId, new CaptureJobRequest { JobDescriptionText = "Software Engineer at Stripe" });

        Assert.IsType<JobCaptureOutcome.Invalid>(outcome);
        Assert.Equal(0, _identifier.CallCount);
    }

    [Fact]
    public async Task CaptureAsync_RejectsTextThatIsNotAPosting()
    {
        _identifier.Result = new PostingIdentity(false, "", "");

        var outcome = await _service.CaptureAsync(_userId, new CaptureJobRequest { JobDescriptionText = Posting });

        Assert.IsType<JobCaptureOutcome.NotAPosting>(outcome);
        Assert.Equal(0, await _fixture.Db.Applications.CountAsync());
    }

    [Fact]
    public async Task CaptureAsync_FlagsAJobAlreadyBeingTracked_UnlessAskedToAddItAnyway()
    {
        _fixture.SeedResume(_userId, layout: TestResumes.Layout());
        await _service.CaptureAsync(_userId, new CaptureJobRequest { JobDescriptionText = Posting });
        _identifier.Result = new PostingIdentity(true, "STRIPE", "software engineer, payments");

        var again = await _service.CaptureAsync(_userId, new CaptureJobRequest { JobDescriptionText = Posting });
        var forced = await _service.CaptureAsync(_userId, new CaptureJobRequest { JobDescriptionText = Posting, AllowDuplicate = true });

        Assert.IsType<JobCaptureOutcome.Duplicate>(again);
        Assert.IsType<JobCaptureOutcome.Captured>(forced);
        Assert.Equal(2, await _fixture.Db.Applications.CountAsync());
    }

    [Fact]
    public async Task CaptureAsync_ReadsALink_WhenNoTextIsPasted()
    {
        var outcome = await _service.CaptureAsync(_userId, new CaptureJobRequest { JobPostingUrl = "https://jobs.example.com/123" });

        var captured = Assert.IsType<JobCaptureOutcome.Captured>(outcome);
        Assert.Equal("Acme", captured.Application.CompanyName);
        Assert.Equal("https://jobs.example.com/123", captured.Application.JobPostingUrl);
        Assert.Equal("https://jobs.example.com/123", _fetcher.LastUrl);
    }

    [Fact]
    public async Task CaptureAsync_SuggestsPasting_WhenALinkCantBeRead()
    {
        _fetcher.Result = new JobPostingFetchOutcome.Failed("That page blocked the request.");

        var outcome = await _service.CaptureAsync(_userId, new CaptureJobRequest { JobPostingUrl = "https://www.linkedin.com/jobs/view/1" });

        Assert.Contains("paste", Assert.IsType<JobCaptureOutcome.NotAPosting>(outcome).Message);
    }
}
