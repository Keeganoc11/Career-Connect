namespace CareerConnect.Api.Domain;

/// <summary>Where a recorded change originated — status transitions and scheduled interviews alike.</summary>
public enum ChangeSource
{
    Manual,

    /// <summary>The user accepted something Gmail scanning suggested.</summary>
    EmailSuggestion,

    /// <summary>
    /// Applied without asking, off a Gmail confirmation. Only ever used for
    /// Preparing → Applied: the user already decided to apply, so a matching
    /// "we received your application" email is confirmation, not a judgement
    /// call. Every other transition still goes through review.
    /// </summary>
    EmailAutomatic
}
