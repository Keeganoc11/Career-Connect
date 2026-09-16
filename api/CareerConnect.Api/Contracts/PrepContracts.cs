using CareerConnect.Api.Domain;

namespace CareerConnect.Api.Contracts;

public class PrepStepResponse
{
    public required string Label { get; init; }
    public required string Detail { get; init; }
    public int? Score { get; init; }
}

public class PrepRunResponse
{
    public required Guid Id { get; init; }
    public required Guid ApplicationId { get; init; }
    public required PrepRunStatus Status { get; init; }
    public required int TargetScore { get; init; }
    public int? BaselineScore { get; init; }
    public int? FinalScore { get; init; }
    public required int Iterations { get; init; }
    public required List<PrepStepResponse> Steps { get; init; }
    public bool? ReadyToApply { get; init; }
    public ResumeReview? Review { get; init; }
    public required List<ResumeChange> Changes { get; init; }
    public string? ErrorMessage { get; init; }
    public required DateTime StartedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }

    public static PrepRunResponse From(PrepRun run) => new()
    {
        Id = run.Id,
        ApplicationId = run.ApplicationId,
        Status = run.Status,
        TargetScore = run.TargetScore,
        BaselineScore = run.BaselineScore,
        FinalScore = run.FinalScore,
        Iterations = run.Iterations,
        Steps = run.Steps
            .Select(s => new PrepStepResponse { Label = s.Label, Detail = s.Detail, Score = s.Score })
            .ToList(),
        ReadyToApply = run.ReadyToApply,
        Review = run.Review,
        Changes = run.Changes,
        ErrorMessage = run.ErrorMessage,
        StartedAtUtc = run.StartedAtUtc,
        CompletedAtUtc = run.CompletedAtUtc,
    };
}

/// <summary>
/// Edits to the documents the prep pipeline produced. Both are the user's to
/// change — the generated text is a starting point, not an output they're
/// stuck with.
/// </summary>
public class UpdateApplicationDocumentsRequest
{
    public string? TailoredResumeText { get; init; }
    public string? CoverLetterText { get; init; }
}
