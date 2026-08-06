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

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public User User { get; set; } = null!;
    public List<StatusChange> StatusHistory { get; set; } = [];
    public List<MatchResult> MatchResults { get; set; } = [];
}
