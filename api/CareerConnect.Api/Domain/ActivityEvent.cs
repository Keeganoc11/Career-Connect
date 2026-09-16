namespace CareerConnect.Api.Domain;

/// <summary>What made the app change something on its own.</summary>
public enum ActivityTrigger
{
    /// <summary>A clear-cut email: a rejection, an interview invite, an application confirmation.</summary>
    Email,

    /// <summary>An application went silent long enough to call it ghosted.</summary>
    Inactivity,
}

/// <summary>
/// One change the app made without asking, kept so it can be seen and undone.
///
/// Automation is only acceptable because it's reversible: every automatic
/// status change writes one of these with enough to put things back exactly —
/// the old status, the date it overwrote, the interview it scheduled.
/// </summary>
public class ActivityEvent
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid ApplicationId { get; set; }

    public ActivityTrigger Trigger { get; set; }

    public ApplicationStatus FromStatus { get; set; }
    public ApplicationStatus ToStatus { get; set; }

    /// <summary>Set when the change also re-dated the application (Preparing → Applied uses the email's date).</summary>
    public DateOnly? PreviousDateApplied { get; set; }

    /// <summary>
    /// The interview scheduled alongside the status change, if one was. Not a
    /// foreign key: the user can delete the interview themselves, and that
    /// mustn't take the record of the change with it.
    /// </summary>
    public Guid? InterviewEventId { get; set; }

    public string? Reasoning { get; set; }
    public string? EmailSubject { get; set; }
    public string? EmailFrom { get; set; }
    public DateTime? EmailReceivedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UndoneAtUtc { get; set; }

    public Application Application { get; set; } = null!;
}
