namespace CareerConnect.Api.Services;

/// <summary>
/// Rewrites an existing resume to fit a specific job posting. Isolated
/// behind an interface so tailoring orchestration is unit-testable without a
/// network call, matching IResumeMatchAnalyzer.
/// </summary>
public interface IResumeTailorer
{
    /// <summary>False when no API key is configured.</summary>
    bool IsConfigured { get; }

    Task<string> TailorAsync(
        string resumeText,
        string jobDescriptionText,
        string roleTitle,
        string companyName,
        CancellationToken cancellationToken = default);
}

/// <summary>Thrown when the model call fails or returns something unusable.</summary>
public class ResumeTailorException(string message, Exception? inner = null)
    : Exception(message, inner);
