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

    /// <summary>
    /// What you found out about the company and the team before the round —
    /// the part of preparing that has nowhere else to live and otherwise ends
    /// up in a text file that's lost by the next interview.
    /// </summary>
    public string? ResearchNotes { get; set; }

    /// <summary>Written after the round, in your own words, before the model sees any of it.</summary>
    public string? Reflection { get; set; }

    /// <summary>1–5, your own read on how it went. Null until the round has happened.</summary>
    public int? SelfRating { get; set; }

    /// <summary>The generated debrief, scored against the job description. Null until one is asked for.</summary>
    public InterviewDebrief? Debrief { get; set; }

    public List<InterviewQuestionEntry> Questions { get; set; } = [];

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
