using CareerConnect.Api.Domain;

namespace CareerConnect.Api.Contracts;

/// <summary>One change the app made on its own, for the "done for you" feed.</summary>
public class ActivityResponse
{
    public required Guid Id { get; init; }
    public required Guid ApplicationId { get; init; }
    public required string CompanyName { get; init; }
    public required string RoleTitle { get; init; }
    public required ActivityTrigger Trigger { get; init; }
    public required ApplicationStatus FromStatus { get; init; }
    public required ApplicationStatus ToStatus { get; init; }

    /// <summary>The interview scheduled with it, while that interview still exists.</summary>
    public DateTime? InterviewAtUtc { get; init; }
    public InterviewKind? InterviewKind { get; init; }

    public string? Reasoning { get; init; }
    public string? EmailSubject { get; init; }
    public string? EmailFrom { get; init; }
    public DateTime? EmailReceivedAtUtc { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public DateTime? UndoneAtUtc { get; init; }

    /// <summary>False once undone, or once the application has moved on to some other status.</summary>
    public required bool CanUndo { get; init; }
}
