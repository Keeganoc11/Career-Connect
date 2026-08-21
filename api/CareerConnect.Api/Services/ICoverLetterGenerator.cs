namespace CareerConnect.Api.Services;

/// <summary>
/// Writes a cover letter grounded in the candidate's actual resume. Isolated
/// behind an interface so orchestration is unit-testable without a network
/// call, matching IResumeTailorer.
/// </summary>
public interface ICoverLetterGenerator
{
    /// <summary>False when no API key is configured.</summary>
    bool IsConfigured { get; }

    Task<string> GenerateAsync(
        string resumeText,
        string jobDescriptionText,
        string roleTitle,
        string companyName,
        CancellationToken cancellationToken = default);
}

/// <summary>Thrown when the model call fails or returns something unusable.</summary>
public class CoverLetterGenerationException(string message, Exception? inner = null)
    : Exception(message, inner);
