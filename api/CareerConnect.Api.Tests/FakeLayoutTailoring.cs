using CareerConnect.Api.Domain;
using CareerConnect.Api.Services;

namespace CareerConnect.Api.Tests;

/// <summary>Stand-in for the Claude-backed layout tailorer so the loop and guard can be tested offline.</summary>
public sealed class FakeResumeLayoutTailorer : IResumeLayoutTailorer
{
    public bool IsConfigured { get; set; } = true;

    public ResumeTailoringException? ThrowOnTailor { get; set; }

    /// <summary>
    /// Edits to propose on each call, in order. Once drained, each call rewrites
    /// the first bullet with a pass-specific phrase so passes are distinguishable.
    /// </summary>
    public Queue<List<LineEdit>> Proposals { get; } = new();

    /// <summary>What ShortenAsync answers with; by default, text cut to the limit.</summary>
    public Func<LineToShorten, string>? Shorten { get; set; }

    public int CallCount { get; private set; }
    public int ShortenCallCount { get; private set; }
    public string? LastInstructions { get; private set; }
    public ResumeLayout? LastCurrent { get; private set; }

    public static readonly string[] PassPhrases =
        ["Designed an inventory API", "Delivered an inventory REST API", "Shipped a production inventory API", "Owned an inventory API"];

    public Task<List<LineEdit>> TailorAsync(
        ResumeLayout current,
        ResumeLayout baseLayout,
        IReadOnlyDictionary<string, int> characterBudgets,
        MatchAnalysis latestScore,
        TailorContext context,
        string? instructions = null,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastInstructions = instructions;
        LastCurrent = current;
        if (ThrowOnTailor is not null)
        {
            throw ThrowOnTailor;
        }

        return Task.FromResult(Proposals.Count > 0
            ? Proposals.Dequeue()
            : [new LineEdit(TestResumes.BulletLine, PassPhrases[(CallCount - 1) % PassPhrases.Length], $"Reason {CallCount}")]);
    }

    public Task<List<LineEdit>> ShortenAsync(
        IReadOnlyList<LineToShorten> lines,
        TailorContext context,
        CancellationToken cancellationToken = default)
    {
        ShortenCallCount++;
        return Task.FromResult(lines
            .Select(l => new LineEdit(l.LineId, Shorten?.Invoke(l) ?? l.Text[..Math.Min(l.Text.Length, l.MaxCharacters)], ""))
            .ToList());
    }
}

public sealed class FakeResumeClaimsAuditor : IResumeClaimsAuditor
{
    public List<UnsupportedClaim> Flag { get; set; } = [];
    public int CallCount { get; private set; }
    public IReadOnlyList<ResumeChange>? LastChanges { get; private set; }

    public Task<List<UnsupportedClaim>> AuditAsync(
        ResumeLayout baseLayout, string? extraFacts, IReadOnlyList<ResumeChange> changes, CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastChanges = changes;
        return Task.FromResult(Flag);
    }
}

public sealed class FakeResumeReviewer : IResumeReviewer
{
    public ResumeReviewDraft Result { get; set; } = new(
        RealityCheck: "Solid junior fit. The cloud requirement is the weak spot.",
        ScoreCeiling: "No production cloud work to point to.",
        Dealbreakers: [],
        Strengths: [new Strength("ASP.NET Core APIs", "Built an inventory API in ASP.NET Core")],
        Gaps: [new Gap("Azure", GapSeverity.Fixable, GapFix.BuildSkill, "Deploy a project to Azure App Service.")],
        WorkOn: [new WorkOnItem("Azure", "Every posting asks for it.", "Deploy the inventory API to Azure.")]);

    public int CallCount { get; private set; }
    public string? LastResumeText { get; private set; }

    public Task<ResumeReviewDraft> ReviewAsync(
        string finalResumeText, int baselineScore, int finalScore, int targetScore, TailorContext context,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastResumeText = finalResumeText;
        return Task.FromResult(Result);
    }
}

public sealed class FakeResumeLayoutReader : IResumeLayoutReader
{
    public ResumeLayoutReadOutcome Result { get; set; } = new ResumeLayoutReadOutcome.Success(TestResumes.Layout());

    public ResumeLayoutReadOutcome Read(byte[] pdf) => Result;
}

public sealed class FakeJobPostingIdentifier : IJobPostingIdentifier
{
    public bool IsConfigured { get; set; } = true;
    public PostingIdentity Result { get; set; } = new(true, "Stripe", "Software Engineer, Payments");
    public int CallCount { get; private set; }

    public Task<PostingIdentity> IdentifyAsync(string text, CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromResult(Result);
    }
}
