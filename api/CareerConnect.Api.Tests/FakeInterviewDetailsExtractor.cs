using CareerConnect.Api.Services;

namespace CareerConnect.Api.Tests;

public sealed class FakeInterviewDetailsExtractor : IInterviewDetailsExtractor
{
    public bool IsConfigured { get; set; } = true;

    /// <summary>What to return, keyed by the index the scanner passed in.</summary>
    public Dictionary<int, (DateTimeOffset? ScheduledAt, string Kind)> ResultByIndex { get; set; } = [];

    public List<InterviewEmailContext> LastEmails { get; private set; } = [];

    public int CallCount { get; private set; }

    public Task<List<ExtractedInterviewDetails>> ExtractAsync(
        List<InterviewEmailContext> emails, CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastEmails = emails;

        var results = emails
            .Where(e => ResultByIndex.ContainsKey(e.Index))
            .Select(e => new ExtractedInterviewDetails(
                e.Index, ResultByIndex[e.Index].ScheduledAt, ResultByIndex[e.Index].Kind))
            .ToList();

        return Task.FromResult(results);
    }
}
