namespace CareerConnect.Api.Domain;

public class Application
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    public required string CompanyName { get; set; }
    public required string RoleTitle { get; set; }
    public string? JobPostingUrl { get; set; }
    public ApplicationStatus Status { get; set; }
    public DateOnly DateApplied { get; set; }
    public string? Notes { get; set; }

    /// <summary>Full pasted job description. Unused in Phase 1; Phase 2's match
    /// scoring consumes it, so it is captured from the start.</summary>
    public string? JobDescriptionText { get; set; }

    /// <summary>Resume rewritten for this specific posting by the prep pipeline.
    /// Deliberately not a Resume row — the library holds base versions the user
    /// maintains, not one throwaway variant per posting.</summary>
    public string? TailoredResumeText { get; set; }

    /// <summary>
    /// The tailored resume in the base resume's layout — what the PDF download
    /// is drawn from. TailoredResumeText is its plain-text form.
    /// </summary>
    public ResumeLayout? TailoredResumeLayout { get; set; }

    public string? CoverLetterText { get; set; }

    /// <summary>Interview prep, as JSON. Persisted rather than regenerated on
    /// every view: it's several model calls, and you want to reread it the
    /// morning of the interview, not pay to recreate it.</summary>
    public string? InterviewPrepJson { get; set; }

    public DateTime? InterviewPrepGeneratedAtUtc { get; set; }

    /// <summary>When the user last said they sent a follow-up. Resets how long the application counts as silent.</summary>
    public DateTime? LastFollowUpAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public User User { get; set; } = null!;
    public List<StatusChange> StatusHistory { get; set; } = [];
    public List<MatchResult> MatchResults { get; set; } = [];
    public List<PrepRun> PrepRuns { get; set; } = [];
    public List<InterviewEvent> Interviews { get; set; } = [];
}
