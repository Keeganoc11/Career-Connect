namespace CareerConnect.Api.Services;

public record JobPostingExtraction(bool IsJobPosting, string CompanyName, string RoleTitle, string JobDescriptionText);

/// <summary>
/// Turns a job posting page's raw text into structured fields. Isolated
/// behind an interface so ingestion orchestration is unit-testable without a
/// network call, matching IResumeMatchAnalyzer / IEmailStatusClassifier.
/// </summary>
public interface IJobPostingExtractor
{
    /// <summary>False when no API key is configured.</summary>
    bool IsConfigured { get; }

    Task<JobPostingExtraction> ExtractAsync(string pageText, CancellationToken cancellationToken = default);
}

/// <summary>Thrown when the model call fails or returns something unusable.</summary>
public class JobPostingExtractionException(string message, Exception? inner = null)
    : Exception(message, inner);
