namespace CareerConnect.Api.Domain;

public enum InterviewKind
{
    PhoneScreen,
    Technical,
    Onsite,
    Final,
    Other
}

/// <summary>
/// A scheduled conversation for one application. Rows rather than a date column
/// on <see cref="Application"/> because a search runs in rounds — a phone screen
/// and the onsite two weeks later are both real, and both need to show up on the
/// calendar. Keeping them as history also means a past interview stays visible
/// after the status has moved on.
/// </summary>
public class InterviewEvent
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }

    public DateTime ScheduledAtUtc { get; set; }
    public InterviewKind Kind { get; set; }

    /// <summary>Interviewer names, video link, what to prepare — whatever the invite said.</summary>
    public string? Notes { get; set; }

    /// <summary>Typed in, or read off an email — the same provenance question status changes answer.</summary>
    public ChangeSource Source { get; set; }

    /// <summary>
    /// Id of the mirrored event on the user's Google Calendar, when calendar
    /// sync is connected. Stored so edits and deletes here follow through to
    /// the calendar instead of stranding a duplicate there. Null means never
    /// synced — either sync is off, or pushing it failed.
    /// </summary>
    public string? CalendarEventId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public Application Application { get; set; } = null!;
}
