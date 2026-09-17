using CareerConnect.Api.Domain;
using CareerConnect.Api.Services;

namespace CareerConnect.Api.Tests;

public sealed class FakeInterviewQuestionSuggester : IInterviewQuestionSuggester
{
    public bool IsConfigured { get; set; } = true;

    public Exception? ThrowOnSuggest { get; set; }

    public List<SuggestedQuestion> Result { get; set; } =
    [
        new(InterviewQuestionSide.TheyAsked, InterviewQuestionKind.Technical, "Walk me through a race condition you fixed."),
        new(InterviewQuestionSide.YouAsk, InterviewQuestionKind.Company, "What does the on-call rotation actually look like?"),
    ];

    /// <summary>What the service said was already listed, so a test can check it isn't asked for twice.</summary>
    public List<string> LastAlreadyListed { get; private set; } = [];

    public int CallCount { get; private set; }

    public Task<List<SuggestedQuestion>> SuggestAsync(
        string companyName,
        string roleTitle,
        InterviewKind kind,
        string jobDescriptionText,
        string resumeText,
        IReadOnlyCollection<string> alreadyListed,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastAlreadyListed = alreadyListed.ToList();

        if (ThrowOnSuggest is not null)
        {
            throw ThrowOnSuggest;
        }

        return Task.FromResult(Result);
    }
}

public sealed class FakeInterviewDebriefWriter : IInterviewDebriefWriter
{
    public bool IsConfigured { get; set; } = true;

    public Exception? ThrowOnWrite { get; set; }

    public DebriefContext? LastContext { get; private set; }

    public InterviewDebrief Result { get; set; } = new()
    {
        Score = 62,
        Verdict = "You answered the .NET questions well and dodged the one about scale.",
        Covered = [new CoveredRequirement("ASP.NET Core", "Described the prep pipeline end to end.")],
        Gaps = [new ProbedGap("Distributed systems", "Never came up in detail.", "Prepare one concrete example.")],
        Practice = ["Tell me about a time you disagreed with a lead."],
        NextRound = ["Read their engineering blog on queues."],
        GeneratedAtUtc = new DateTime(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc),
    };

    public Task<InterviewDebrief> WriteAsync(DebriefContext context, CancellationToken cancellationToken = default)
    {
        LastContext = context;

        if (ThrowOnWrite is not null)
        {
            throw ThrowOnWrite;
        }

        return Task.FromResult(Result);
    }
}
