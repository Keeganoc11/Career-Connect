using CareerConnect.Api.Domain;

namespace CareerConnect.Api.Contracts;

/// <summary>Why an application is being surfaced. Drives the icon and tone in the UI.</summary>
public enum NudgeKind
{
    /// <summary>Prep cleared the bar and then nothing happened.</summary>
    ReadyToApply,

    /// <summary>Found the posting, never prepped it.</summary>
    NeverPrepped,

    /// <summary>No movement since applying.</summary>
    Silent,

    /// <summary>Silent long enough that it's probably over.</summary>
    ProbablyGhosted,

    /// <summary>An offer waiting on a decision.</summary>
    AwaitingYou,
}

public class AgendaNudgeResponse
{
    public required Guid ApplicationId { get; init; }
    public required string CompanyName { get; init; }
    public required string RoleTitle { get; init; }
    public required ApplicationStatus Status { get; init; }
    public required NudgeKind Kind { get; init; }

    /// <summary>Days since the application last moved. Zero for interview-driven nudges.</summary>
    public required int DaysSinceActivity { get; init; }

    public required string Message { get; init; }
}

public class UpcomingInterviewResponse
{
    public required Guid InterviewId { get; init; }
    public required Guid ApplicationId { get; init; }
    public required string CompanyName { get; init; }
    public required string RoleTitle { get; init; }
    public required DateTime ScheduledAtUtc { get; init; }
    public required InterviewKind Kind { get; init; }
    public string? Notes { get; init; }
    public required bool OnCalendar { get; init; }

    /// <summary>Whether interview prep has been generated and saved for this application.</summary>
    public required bool HasPrep { get; init; }
}

public class AgendaResponse
{
    public required List<UpcomingInterviewResponse> UpcomingInterviews { get; init; }
    public required List<AgendaNudgeResponse> Nudges { get; init; }
}
