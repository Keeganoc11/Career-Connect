namespace CareerConnect.Api.Domain;

/// <summary>
/// One LLM-produced comparison of a resume against an application's job
/// description. Keyed to both so re-scoring after a resume edit is meaningful
/// and prior scores stay interpretable — results are never overwritten.
/// </summary>
public class MatchResult
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid ResumeId { get; set; }

    /// <summary>0-100. Higher means the resume covers more of what the posting asks for.</summary>
    public int Score { get; set; }

    public required string Summary { get; set; }

    /// <summary>Requirements the resume already evidences.</summary>
    public List<string> MatchedKeywords { get; set; } = [];

    /// <summary>Requirements the posting asks for that the resume doesn't show.</summary>
    public List<string> MissingKeywords { get; set; } = [];

    /// <summary>Concrete, copy-pasteable edits worth making before applying.</summary>
    public List<SuggestedEdit> Suggestions { get; set; } = [];

    /// <summary>Model that produced this result — scores aren't comparable across models.</summary>
    public required string ModelId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public Application Application { get; set; } = null!;
    public Resume Resume { get; set; } = null!;
}
