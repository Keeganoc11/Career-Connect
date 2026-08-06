namespace CareerConnect.Api.Contracts;

public class SuggestedEditResponse
{
    public required string Section { get; init; }
    public required string Guidance { get; init; }
    public required string SuggestedText { get; init; }
}

public class MatchResultResponse
{
    public required Guid Id { get; init; }
    public required Guid ApplicationId { get; init; }
    public required Guid ResumeId { get; init; }
    public required string ResumeLabel { get; init; }
    public required int Score { get; init; }
    public required string Summary { get; init; }
    public required List<string> MatchedKeywords { get; init; }
    public required List<string> MissingKeywords { get; init; }
    public required List<SuggestedEditResponse> Suggestions { get; init; }
    public required string ModelId { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
}

/// <summary>
/// Why a scoring request couldn't run. Distinguished from transport failures so
/// the client can tell the user what to fix rather than just "try again".
/// </summary>
public enum MatchFailureReason
{
    ApplicationNotFound,
    NoJobDescription,
    NoActiveResume,
    AnalyzerUnavailable,
    AnalyzerFailed
}

public class MatchFailure
{
    public required MatchFailureReason Reason { get; init; }
    public required string Message { get; init; }
}
