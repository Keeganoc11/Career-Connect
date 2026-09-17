namespace CareerConnect.Api.Domain;

/// <summary>The bottom line on whether a posting is worth the candidate's time.</summary>
public enum FitVerdict
{
    StrongFit,
    WorthAShot,
    Stretch,
    NotAFit,
}

public enum GapSeverity
{
    /// <summary>A hard requirement the candidate clearly doesn't meet. No rewrite fixes it.</summary>
    Dealbreaker,
    /// <summary>Real, but closable — through wording, an interview answer, or some study.</summary>
    Fixable,
    Minor,
}

public enum GapFix
{
    /// <summary>The experience exists; the resume just doesn't show it.</summary>
    Resume,
    /// <summary>Can't go on the page honestly, but can be addressed in conversation.</summary>
    Interview,
    /// <summary>Only closes by actually learning or building something.</summary>
    BuildSkill,
}

public record Dealbreaker(string Requirement, string Why);

/// <summary>A strength, with the resume line that proves it — no evidence, no strength.</summary>
public record Strength(string Point, string Evidence);

public record Gap(string Requirement, GapSeverity Severity, GapFix Fix, string Advice);

public record WorkOnItem(string Skill, string Why, string NextStep);

/// <summary>
/// The honest read on one application, written after tailoring has done all
/// it can. Persisted as JSON on the prep run.
/// </summary>
public class ResumeReview
{
    public required FitVerdict Verdict { get; init; }

    /// <summary>A few blunt sentences. If this isn't a realistic fit, it says so and says why.</summary>
    public required string RealityCheck { get; init; }

    /// <summary>Why tailoring couldn't push the score higher than it did.</summary>
    public required string ScoreCeiling { get; init; }

    public List<Dealbreaker> Dealbreakers { get; init; } = [];
    public List<Strength> Strengths { get; init; } = [];
    public List<Gap> Gaps { get; init; } = [];
    public List<WorkOnItem> WorkOn { get; init; } = [];
}

/// <summary>One line tailoring changed, and why — the "what changed" list on the result.</summary>
/// <param name="Swap">
/// Set on every line of an entry swapped in from extra facts, e.g.
/// "Drinks Around The World IOS Application → Career Connect Application",
/// so the result can show the swap as one change.
/// </param>
public record ResumeChange(string LineId, string Before, string After, string Reason, string? Swap = null);

public static class FitVerdicts
{
    /// <summary>
    /// Decided here, not by the model: the score bands are fixed, and a hard
    /// requirement the candidate doesn't meet caps the verdict however well the
    /// rewrite scored. A 90 with a "10+ years required" in the way is not a
    /// strong fit, and saying otherwise is exactly the flattery this exists to avoid.
    /// </summary>
    public static FitVerdict From(int score, int dealbreakerCount)
    {
        var byScore = score switch
        {
            >= 80 => FitVerdict.StrongFit,
            >= 65 => FitVerdict.WorthAShot,
            >= 50 => FitVerdict.Stretch,
            _ => FitVerdict.NotAFit,
        };

        return dealbreakerCount switch
        {
            0 => byScore,
            1 => byScore < FitVerdict.Stretch ? FitVerdict.Stretch : byScore,
            _ => FitVerdict.NotAFit,
        };
    }
}
