using CareerConnect.Api.Domain;

namespace CareerConnect.Api.Services;

/// <summary>
/// Mirrors scheduled interviews onto the user's real calendar.
/// <para>
/// Every method swallows its own failures and returns null rather than
/// throwing. Scheduling an interview must succeed whether or not the calendar
/// is reachable — a Google outage is not a reason to lose the appointment, and
/// surfacing it as a save error would teach the user to distrust the button.
/// </para>
/// </summary>
public interface IInterviewCalendarSync
{
    /// <summary>
    /// Creates or updates the calendar copy of an interview. Returns the
    /// calendar's event id to store, or null when sync is off (no connection,
    /// or a token predating calendar consent) or the write failed.
    /// </summary>
    Task<string?> UpsertAsync(
        Guid userId, InterviewEvent interview, Application application, CancellationToken cancellationToken = default);

    /// <summary>Removes the calendar copy. Best-effort; a stranded event is not worth failing a delete over.</summary>
    Task DeleteAsync(Guid userId, string calendarEventId, CancellationToken cancellationToken = default);
}
