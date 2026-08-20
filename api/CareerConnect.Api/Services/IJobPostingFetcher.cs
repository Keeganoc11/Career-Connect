namespace CareerConnect.Api.Services;

public abstract record JobPostingFetchOutcome
{
    public sealed record Success(string Text) : JobPostingFetchOutcome;
    public sealed record Failed(string Message) : JobPostingFetchOutcome;
}

/// <summary>
/// Fetches a URL the user pasted in and reduces it to plain readable text.
/// Isolated behind an interface so the SSRF-sensitive networking code never
/// has to be exercised by unit tests.
/// </summary>
public interface IJobPostingFetcher
{
    Task<JobPostingFetchOutcome> FetchAsync(string url, CancellationToken cancellationToken = default);
}
