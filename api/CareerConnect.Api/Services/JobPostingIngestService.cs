namespace CareerConnect.Api.Services;

public enum JobPostingIngestFailureReason
{
    FetchFailed,
    NotAJobPosting,
    ExtractorUnavailable,
    ExtractorFailed,
}

public abstract record JobPostingIngestOutcome
{
    public sealed record Success(string CompanyName, string RoleTitle, string JobDescriptionText) : JobPostingIngestOutcome;
    public sealed record Failed(JobPostingIngestFailureReason Reason, string Message) : JobPostingIngestOutcome;
}

public interface IJobPostingIngestService
{
    Task<JobPostingIngestOutcome> IngestAsync(string url, CancellationToken cancellationToken = default);
}

public class JobPostingIngestService(IJobPostingFetcher fetcher, IJobPostingExtractor extractor) : IJobPostingIngestService
{
    public async Task<JobPostingIngestOutcome> IngestAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!extractor.IsConfigured)
        {
            return new JobPostingIngestOutcome.Failed(
                JobPostingIngestFailureReason.ExtractorUnavailable,
                "Filling in from a URL needs an Anthropic API key. See the README for setup.");
        }

        var fetched = await fetcher.FetchAsync(url, cancellationToken);
        if (fetched is not JobPostingFetchOutcome.Success success)
        {
            var message = fetched is JobPostingFetchOutcome.Failed failed
                ? failed.Message
                : "Couldn't read that page.";
            return new JobPostingIngestOutcome.Failed(JobPostingIngestFailureReason.FetchFailed, message);
        }

        JobPostingExtraction extraction;
        try
        {
            extraction = await extractor.ExtractAsync(success.Text, cancellationToken);
        }
        catch (JobPostingExtractionException ex)
        {
            return new JobPostingIngestOutcome.Failed(JobPostingIngestFailureReason.ExtractorFailed, ex.Message);
        }

        if (!extraction.IsJobPosting)
        {
            return new JobPostingIngestOutcome.Failed(
                JobPostingIngestFailureReason.NotAJobPosting,
                "That page doesn't look like a single job posting. Try the direct link to the role, or paste the description in yourself.");
        }

        return new JobPostingIngestOutcome.Success(
            extraction.CompanyName, extraction.RoleTitle, extraction.JobDescriptionText);
    }
}
