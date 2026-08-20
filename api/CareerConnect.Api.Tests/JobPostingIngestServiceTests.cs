using CareerConnect.Api.Services;

namespace CareerConnect.Api.Tests;

public sealed class JobPostingIngestServiceTests
{
    private readonly FakeJobPostingFetcher _fetcher = new();
    private readonly FakeJobPostingExtractor _extractor = new();
    private readonly JobPostingIngestService _service;

    public JobPostingIngestServiceTests()
    {
        _service = new JobPostingIngestService(_fetcher, _extractor);
    }

    [Fact]
    public async Task IngestAsync_ReturnsExtractedFields_OnSuccess()
    {
        var outcome = await _service.IngestAsync("https://jobs.example.com/123");

        var success = Assert.IsType<JobPostingIngestOutcome.Success>(outcome);
        Assert.Equal("Acme", success.CompanyName);
        Assert.Equal("Senior Backend Engineer", success.RoleTitle);
        Assert.Equal("https://jobs.example.com/123", _fetcher.LastUrl);
    }

    [Fact]
    public async Task IngestAsync_FailsWithoutCallingExtractor_WhenExtractorNotConfigured()
    {
        _extractor.IsConfigured = false;

        var outcome = await _service.IngestAsync("https://jobs.example.com/123");

        var failed = Assert.IsType<JobPostingIngestOutcome.Failed>(outcome);
        Assert.Equal(JobPostingIngestFailureReason.ExtractorUnavailable, failed.Reason);
        Assert.Null(_extractor.LastPageText);
    }

    [Fact]
    public async Task IngestAsync_FailsWithoutCallingExtractor_WhenFetchFails()
    {
        _fetcher.Result = new JobPostingFetchOutcome.Failed("Couldn't reach that URL.");

        var outcome = await _service.IngestAsync("https://jobs.example.com/123");

        var failed = Assert.IsType<JobPostingIngestOutcome.Failed>(outcome);
        Assert.Equal(JobPostingIngestFailureReason.FetchFailed, failed.Reason);
        Assert.Equal("Couldn't reach that URL.", failed.Message);
        Assert.Null(_extractor.LastPageText);
    }

    [Fact]
    public async Task IngestAsync_FailsAsNotAJobPosting_WhenExtractorSaysSo()
    {
        _extractor.Result = new JobPostingExtraction(
            IsJobPosting: false, CompanyName: "", RoleTitle: "", JobDescriptionText: "");

        var outcome = await _service.IngestAsync("https://example.com/blog/post");

        var failed = Assert.IsType<JobPostingIngestOutcome.Failed>(outcome);
        Assert.Equal(JobPostingIngestFailureReason.NotAJobPosting, failed.Reason);
    }

    [Fact]
    public async Task IngestAsync_FailsAsExtractorFailed_WhenExtractorThrows()
    {
        _extractor.ThrowOnExtract = new JobPostingExtractionException("The extraction request to Claude failed.");

        var outcome = await _service.IngestAsync("https://jobs.example.com/123");

        var failed = Assert.IsType<JobPostingIngestOutcome.Failed>(outcome);
        Assert.Equal(JobPostingIngestFailureReason.ExtractorFailed, failed.Reason);
    }

    [Fact]
    public async Task IngestAsync_PassesFetchedTextToExtractor()
    {
        _fetcher.Result = new JobPostingFetchOutcome.Success("some scraped page text");

        await _service.IngestAsync("https://jobs.example.com/123");

        Assert.Equal("some scraped page text", _extractor.LastPageText);
    }
}
