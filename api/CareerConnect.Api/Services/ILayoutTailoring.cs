using CareerConnect.Api.Domain;

namespace CareerConnect.Api.Services;

/// <summary>The posting, and what the candidate has said about themselves beyond the page.</summary>
public record TailorContext(string JobDescription, string RoleTitle, string CompanyName, string? ExtraFacts);

/// <summary>A proposed replacement for one line's editable words.</summary>
public record LineEdit(string LineId, string Text, string Reason);

/// <summary>A rewrite that didn't fill its space exactly, handed back to be adjusted.</summary>
/// <param name="TooShort">True when it left a row empty rather than running over.</param>
public record LineToFit(string LineId, string Text, int MinCharacters, int MaxCharacters, int Rows, bool TooShort);

/// <summary>A changed line that claims something the base resume and extra facts don't back up.</summary>
public record UnsupportedClaim(string LineId, string Reason);

/// <summary>The model's side of the review. The verdict is decided in code from the score and dealbreakers.</summary>
public record ResumeReviewDraft(
    string RealityCheck,
    string ScoreCeiling,
    List<Dealbreaker> Dealbreakers,
    List<Strength> Strengths,
    List<Gap> Gaps,
    List<WorkOnItem> WorkOn);

public interface IResumeLayoutTailorer
{
    bool IsConfigured { get; }

    /// <summary>
    /// Proposes new words for editable lines of <paramref name="current"/>,
    /// steered by the latest score. Proposals only — every one is checked
    /// before it touches the resume.
    /// </summary>
    Task<List<LineEdit>> TailorAsync(
        ResumeLayout current,
        ResumeLayout baseLayout,
        IReadOnlyDictionary<string, CharacterRange> characterBudgets,
        MatchAnalysis latestScore,
        TailorContext context,
        string? instructions = null,
        CancellationToken cancellationToken = default);

    Task<List<LineEdit>> FitAsync(
        IReadOnlyList<LineToFit> lines,
        TailorContext context,
        CancellationToken cancellationToken = default);
}

public interface IResumeClaimsAuditor
{
    /// <summary>Checks every changed line against the base resume and extra facts.</summary>
    Task<List<UnsupportedClaim>> AuditAsync(
        ResumeLayout baseLayout,
        string? extraFacts,
        IReadOnlyList<ResumeChange> changes,
        CancellationToken cancellationToken = default);
}

public interface IResumeReviewer
{
    Task<ResumeReviewDraft> ReviewAsync(
        string finalResumeText,
        int baselineScore,
        int finalScore,
        int targetScore,
        TailorContext context,
        CancellationToken cancellationToken = default);
}
