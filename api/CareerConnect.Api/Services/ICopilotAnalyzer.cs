namespace CareerConnect.Api.Services;

public record CopilotApplicationSnapshot(
    int Index,
    string CompanyName,
    string RoleTitle,
    string Status,
    DateOnly DateApplied,
    DateTime LastActivityUtc,
    int? LatestMatchScore);

public record CopilotPipelineSnapshot(
    List<CopilotApplicationSnapshot> Applications,
    bool HasActiveResume,
    DateTime NowUtc);

public record CopilotAction(string Title, string Detail, string Priority, int? ApplicationIndex);

public record CopilotInsights(string OverallSummary, List<CopilotAction> Actions);

/// <summary>
/// Synthesizes a "what needs attention" view across a user's whole pipeline.
/// Isolated behind an interface so orchestration is unit-testable without a
/// network call, matching the other Claude-backed services.
/// </summary>
public interface ICopilotAnalyzer
{
    /// <summary>False when no API key is configured.</summary>
    bool IsConfigured { get; }

    Task<CopilotInsights> AnalyzeAsync(CopilotPipelineSnapshot snapshot, CancellationToken cancellationToken = default);
}

/// <summary>Thrown when the model call fails or returns something unusable.</summary>
public class CopilotAnalysisException(string message, Exception? inner = null)
    : Exception(message, inner);
