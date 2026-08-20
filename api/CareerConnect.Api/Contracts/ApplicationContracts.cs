using System.ComponentModel.DataAnnotations;
using CareerConnect.Api.Domain;

namespace CareerConnect.Api.Contracts;

public class CreateApplicationRequest
{
    [Required, MaxLength(200)]
    public required string CompanyName { get; init; }

    [Required, MaxLength(200)]
    public required string RoleTitle { get; init; }

    [Url, MaxLength(2048)]
    public string? JobPostingUrl { get; init; }

    public ApplicationStatus Status { get; init; } = ApplicationStatus.Applied;

    [Required]
    public DateOnly DateApplied { get; init; }

    public string? Notes { get; init; }

    public string? JobDescriptionText { get; init; }
}

/// <summary>Status is deliberately absent — it changes only via the dedicated
/// PATCH endpoint so every transition is recorded in status history.</summary>
public class UpdateApplicationRequest
{
    [Required, MaxLength(200)]
    public required string CompanyName { get; init; }

    [Required, MaxLength(200)]
    public required string RoleTitle { get; init; }

    [Url, MaxLength(2048)]
    public string? JobPostingUrl { get; init; }

    [Required]
    public DateOnly DateApplied { get; init; }

    public string? Notes { get; init; }

    public string? JobDescriptionText { get; init; }
}

public class ExtractJobPostingRequest
{
    [Required, Url, MaxLength(2048)]
    public required string Url { get; init; }
}

public class JobPostingExtractionResponse
{
    public required string CompanyName { get; init; }
    public required string RoleTitle { get; init; }
    public required string JobDescriptionText { get; init; }
}

public class TailorResumeRequest
{
    [Required]
    public required string ResumeContent { get; init; }
}

public class TailoredResumeResponse
{
    public required string Content { get; init; }
}

public class UpdateStatusRequest
{
    [Required]
    public required ApplicationStatus Status { get; init; }
}

public class StatusChangeResponse
{
    public ApplicationStatus? FromStatus { get; init; }
    public required ApplicationStatus ToStatus { get; init; }
    public required DateTime ChangedAtUtc { get; init; }
    public required StatusChangeSource Source { get; init; }
}

public class ApplicationResponse
{
    public required Guid Id { get; init; }
    public required string CompanyName { get; init; }
    public required string RoleTitle { get; init; }
    public string? JobPostingUrl { get; init; }
    public required ApplicationStatus Status { get; init; }
    public required DateOnly DateApplied { get; init; }
    public string? Notes { get; init; }
    public string? JobDescriptionText { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public required DateTime UpdatedAtUtc { get; init; }
    public List<StatusChangeResponse>? StatusHistory { get; init; }
}

public class StatusCountResponse
{
    public required ApplicationStatus Status { get; init; }
    public required int Count { get; init; }
}

public class SummaryResponse
{
    public required int Total { get; init; }
    public required List<StatusCountResponse> Counts { get; init; }
}
