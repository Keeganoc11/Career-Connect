namespace CareerConnect.Api.Domain;

/// <summary>A requirement from the posting that the round actually covered, and what showed it.</summary>
public record CoveredRequirement(string Requirement, string Evidence);

/// <summary>Something the posting asks for that the round exposed, with what to do about it.</summary>
public record ProbedGap(string Requirement, string WhatHappened, string Fix);

/// <summary>
/// The honest read on one interview, written after it happened and scored
/// against the job description — the interview counterpart to
/// <see cref="ResumeReview"/>, and deliberately in the same voice: it says when
/// a round went badly rather than finding something encouraging to say.
///
/// Persisted as JSON on the interview; nothing queries inside it.
/// </summary>
public class InterviewDebrief
{
    /// <summary>0–100, how well this round answered what the posting asks for.</summary>
    public required int Score { get; init; }

    /// <summary>A few blunt sentences on how the round actually went.</summary>
    public required string Verdict { get; init; }

    public List<CoveredRequirement> Covered { get; init; } = [];
    public List<ProbedGap> Gaps { get; init; } = [];

    /// <summary>Answers worth rehearsing before the next round, in the words of the question.</summary>
    public List<string> Practice { get; init; } = [];

    /// <summary>What to prepare for whatever comes next, if anything does.</summary>
    public List<string> NextRound { get; init; } = [];

    public required DateTime GeneratedAtUtc { get; init; }
}
