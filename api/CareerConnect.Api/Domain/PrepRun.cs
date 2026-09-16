namespace CareerConnect.Api.Domain;

public enum PrepRunStatus
{
    Running,
    Succeeded,
    Failed
}

/// <summary>One step the pipeline finished, in order, for the live progress view.</summary>
public record PrepStep(string Label, string Detail, int? Score);

/// <summary>
/// One automated prep pass over an application: score the base resume, tailor
/// and re-score until it clears the target, then write a cover letter.
///
/// Persisted rather than held in the request because a full pass is several
/// chained LLM calls — far too long to hold an HTTP connection open for, and
/// the user should be able to close the tab and come back to the result.
/// </summary>
public class PrepRun
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }

    public PrepRunStatus Status { get; set; }

    /// <summary>Score the target is trying to clear. Stored per run so an old run stays interpretable after the default changes.</summary>
    public int TargetScore { get; set; }

    /// <summary>Score of the untouched active resume, before any tailoring.</summary>
    public int? BaselineScore { get; set; }

    /// <summary>Best score reached — from the tailored rewrite if it beat the baseline, otherwise the baseline.</summary>
    public int? FinalScore { get; set; }

    /// <summary>Tailoring passes actually run. Zero means the base resume already cleared the target.</summary>
    public int Iterations { get; set; }

    public List<PrepStep> Steps { get; set; } = [];

    /// <summary>Null while running. Set once the pipeline knows whether the target was cleared.</summary>
    public bool? ReadyToApply { get; set; }

    /// <summary>The reality check, written once tailoring has done all it can. Null while running or on failure.</summary>
    public ResumeReview? Review { get; set; }

    /// <summary>Every line the final version changed from the base resume, with the reason.</summary>
    public List<ResumeChange> Changes { get; set; } = [];

    public string? ErrorMessage { get; set; }

    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public Application Application { get; set; } = null!;
}
