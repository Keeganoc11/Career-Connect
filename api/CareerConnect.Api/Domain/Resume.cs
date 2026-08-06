namespace CareerConnect.Api.Domain;

/// <summary>
/// A stored resume version. Multiple are allowed so tailored resumes can be
/// compared; exactly one is marked active and used for new match scoring.
/// </summary>
public class Resume
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    public required string Label { get; set; }
    public required string Content { get; set; }
    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public User User { get; set; } = null!;
    public List<MatchResult> MatchResults { get; set; } = [];
}
