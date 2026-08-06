using System.ComponentModel.DataAnnotations;

namespace CareerConnect.Api.Contracts;

public class SaveResumeRequest
{
    [Required, MaxLength(200)]
    public required string Label { get; init; }

    [Required, MinLength(50, ErrorMessage = "Resume text looks too short to score against a job description.")]
    public required string Content { get; init; }
}

public class ResumeResponse
{
    public required Guid Id { get; init; }
    public required string Label { get; init; }
    public required string Content { get; init; }
    public required bool IsActive { get; init; }
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
    public required DateTime UpdatedAtUtc { get; init; }
}
