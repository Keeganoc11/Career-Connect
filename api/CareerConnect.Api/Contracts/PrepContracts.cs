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
    /// <summary>What the user asked this pass to do differently, if anything.</summary>
    public string? Instructions { get; init; }
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
        Instructions = run.Instructions,
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

public class StartPrepRequest
{
    /// <summary>
    /// "Lean more backend", "don't mention Kubernetes". When set, the pass
    /// continues from the current tailored version rather than starting over,
    /// and always makes at least one rewrite.
    /// </summary>
    [System.ComponentModel.DataAnnotations.MaxLength(1000)]
    public string? Instructions { get; init; }
}

public class CaptureJobRequest
{
    [System.ComponentModel.DataAnnotations.MaxLength(60000)]
    public string? JobDescriptionText { get; init; }

    [System.ComponentModel.DataAnnotations.Url, System.ComponentModel.DataAnnotations.MaxLength(2048)]
    public string? JobPostingUrl { get; init; }

    /// <summary>The user's own date, so an evening paste isn't dated tomorrow in UTC.</summary>
    public DateOnly? LocalDate { get; init; }

    /// <summary>Create it even though the same company and role are already tracked.</summary>
    public bool AllowDuplicate { get; init; }
}

public class CaptureJobResponse
{
    public required ApplicationResponse Application { get; init; }
    public PrepRunResponse? PrepRun { get; init; }

    /// <summary>Why tailoring didn't start, when it didn't — the application was still saved.</summary>
    public string? PrepMessage { get; init; }
}
