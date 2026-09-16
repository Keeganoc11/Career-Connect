namespace CareerConnect.Api.Domain;

/// <summary>Where a recorded change originated — status transitions and scheduled interviews alike.</summary>
public enum ChangeSource
{
    Manual,

    /// <summary>The user accepted something Gmail scanning suggested.</summary>
    EmailSuggestion,

    /// <summary>
    /// Applied without asking, off a clear-cut email: a confirmation for an
    /// application being prepared, a rejection, or an invite that moves the
    /// process forward. Anything less certain still goes through review, and
    /// every one of these can be undone.
    /// </summary>
    EmailAutomatic,

    /// <summary>
    /// Marked ghosted after a long silence. Undoable, like every change the app
    /// makes on its own (see <see cref="ActivityEvent"/>).
    /// </summary>
    Inactivity,
}
