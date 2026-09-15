using CareerConnect.Api.Domain;
using CareerConnect.Api.Services;

namespace CareerConnect.Api.Tests;

/// <summary>
/// Stand-in for Google Calendar. <see cref="NextEventId"/> null simulates sync
/// being off (no connection, or a token predating calendar consent).
/// </summary>
public sealed class FakeInterviewCalendarSync : IInterviewCalendarSync
{
    /// <summary>What UpsertAsync hands back. Null means "sync is off".</summary>
    public string? NextEventId { get; set; } = "cal-event-1";

    public List<Guid> UpsertedInterviewIds { get; } = [];
    public List<string> DeletedEventIds { get; } = [];

    /// <summary>The real sync swallows its own failures; setting this proves callers survive one that leaks.</summary>
    public Exception? ThrowOnUpsert { get; set; }

    public Task<string?> UpsertAsync(
        Guid userId, InterviewEvent interview, Application application, CancellationToken cancellationToken = default)
    {
        if (ThrowOnUpsert is not null)
        {
            throw ThrowOnUpsert;
        }

        UpsertedInterviewIds.Add(interview.Id);
        return Task.FromResult(NextEventId);
    }

    public Task DeleteAsync(Guid userId, string calendarEventId, CancellationToken cancellationToken = default)
    {
        DeletedEventIds.Add(calendarEventId);
        return Task.CompletedTask;
    }
}
