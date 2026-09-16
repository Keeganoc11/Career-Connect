using System.ComponentModel.DataAnnotations;

namespace CareerConnect.Api.Contracts;

public class SaveResumeRequest
{
    [Required, MaxLength(200)]
    public required string Label { get; init; }

    [Required, MinLength(50, ErrorMessage = "Resume text looks too short to score against a job description.")]
    public required string Content { get; init; }
}

public record ResumeResponse
{
    public required Guid Id { get; init; }
    public required string Label { get; init; }
    public required string Content { get; init; }
    public required bool IsActive { get; init; }

    /// <summary>True when this came from a PDF whose layout was read — the only kind tailoring can use.</summary>
    public required bool HasLayout { get; init; }

    public string? ExtraFacts { get; init; }

    /// <summary>Set on upload only: why the file's layout couldn't be kept, when it couldn't.</summary>
    public string? LayoutWarning { get; init; }

    public required DateTime CreatedAtUtc { get; init; }
    public required DateTime UpdatedAtUtc { get; init; }
}

/// <summary>List item — omits Content so the list endpoint stays small.</summary>
public class ResumeSummaryResponse
{
    public required Guid Id { get; init; }
    public required string Label { get; init; }
    public required bool IsActive { get; init; }
    public required int CharacterCount { get; init; }
    public required bool HasLayout { get; init; }
    public required DateTime UpdatedAtUtc { get; init; }
}

public class UpdateExtraFactsRequest
{
    [MaxLength(4000)]
    public string? ExtraFacts { get; init; }
}
