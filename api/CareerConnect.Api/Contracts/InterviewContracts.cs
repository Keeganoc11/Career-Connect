using System.ComponentModel.DataAnnotations;
using CareerConnect.Api.Domain;

namespace CareerConnect.Api.Contracts;

public class CreateInterviewRequest
{
    [Required]
    public required DateTime ScheduledAtUtc { get; init; }

    public InterviewKind Kind { get; init; } = InterviewKind.Other;

    [MaxLength(4000)]
    public string? Notes { get; init; }
}

public class UpdateInterviewRequest
{
    [Required]
    public required DateTime ScheduledAtUtc { get; init; }

    public InterviewKind Kind { get; init; } = InterviewKind.Other;

    [MaxLength(4000)]
    public string? Notes { get; init; }
}

public class InterviewEventResponse
{
    public required Guid Id { get; init; }
    public required Guid ApplicationId { get; init; }
    public required DateTime ScheduledAtUtc { get; init; }
    public required InterviewKind Kind { get; init; }
    public string? Notes { get; init; }
    public required ChangeSource Source { get; init; }

    /// <summary>True once this made it onto the connected Google Calendar.</summary>
    public required bool OnCalendar { get; init; }
}
