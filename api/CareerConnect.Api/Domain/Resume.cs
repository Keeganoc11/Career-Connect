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

    /// <summary>
    /// The uploaded PDF read into positioned lines. Present only for PDF
    /// uploads it could read — it's what lets a tailored resume come out in the
    /// original's exact format. Content is derived from it when it exists.
    /// </summary>
    public ResumeLayout? Layout { get; set; }

    /// <summary>
    /// True things about the candidate that don't fit on the page. Tailoring may
    /// draw on them; the claims check treats them as evidence alongside the resume.
    /// </summary>
    public string? ExtraFacts { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public User User { get; set; } = null!;
    public List<MatchResult> MatchResults { get; set; } = [];
}
