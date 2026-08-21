using CareerConnect.Api.Services;

namespace CareerConnect.Api.Tests;

public sealed class FakeInterviewPrepGenerator : IInterviewPrepGenerator
{
    public bool IsConfigured { get; set; } = true;

    public InterviewPrepGenerationException? ThrowOnGenerate { get; set; }

    public InterviewPrep Result { get; set; } = new(
        [new InterviewQuestion("Tell me about a time you debugged a production issue.", "Standard behavioral question for this level.")],
        [new TalkingPoint("SQL automation work at DataCorp", "Bring it up when asked about data-layer experience.")]);

    public int CallCount { get; private set; }

    public Task<InterviewPrep> GenerateAsync(
        string resumeText, string jobDescriptionText, string roleTitle, string companyName,
        CancellationToken cancellationToken = default)
    {
        CallCount++;

        if (ThrowOnGenerate is not null)
        {
            throw ThrowOnGenerate;
        }

        return Task.FromResult(Result);
    }
}
