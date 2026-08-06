using CareerConnect.Api.Domain;

namespace CareerConnect.Api.Services;

/// <summary>Structured analysis returned by the LLM for one resume/JD pair.</summary>
public record MatchAnalysis(
    int Score,
    string Summary,
    List<string> MatchedKeywords,
    List<string> MissingKeywords,
    List<SuggestedEdit> Suggestions,
    string ModelId);

/// <summary>
/// Isolates the LLM call behind an interface so the scoring service can be unit
/// tested without network access or an API key.
/// </summary>
public interface IResumeMatchAnalyzer
{
    /// <summary>False when no API key is configured — the app still runs, scoring is just disabled.</summary>
    bool IsConfigured { get; }

    Task<MatchAnalysis> AnalyzeAsync(
        string resumeText,
        string jobDescriptionText,
        string roleTitle,
        string companyName,
        CancellationToken cancellationToken = default);
}

/// <summary>Thrown when the model call fails or returns something unusable.</summary>
public class MatchAnalysisException(string message, Exception? inner = null)
    : Exception(message, inner);
