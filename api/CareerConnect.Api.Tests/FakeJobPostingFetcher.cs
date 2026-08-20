using CareerConnect.Api.Services;

namespace CareerConnect.Api.Tests;

/// <summary>
/// Stand-in for the real (network- and SSRF-guard-heavy) fetcher so ingest
/// orchestration can be tested without making an HTTP call.
/// </summary>
public sealed class FakeJobPostingFetcher : IJobPostingFetcher
{
    public JobPostingFetchOutcome Result { get; set; } =
        new JobPostingFetchOutcome.Success("Senior Backend Engineer at Acme. We need someone with ASP.NET Core experience.");

    public string? LastUrl { get; private set; }

    public Task<JobPostingFetchOutcome> FetchAsync(string url, CancellationToken cancellationToken = default)
    {
        LastUrl = url;
        return Task.FromResult(Result);
    }
}
