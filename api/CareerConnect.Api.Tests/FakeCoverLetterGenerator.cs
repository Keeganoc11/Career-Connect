using CareerConnect.Api.Services;

namespace CareerConnect.Api.Tests;

public sealed class FakeCoverLetterGenerator : ICoverLetterGenerator
{
    public bool IsConfigured { get; set; } = true;

    public CoverLetterGenerationException? ThrowOnGenerate { get; set; }

    public string Result { get; set; } = "Dear Hiring Team, ...";

    public int CallCount { get; private set; }

    public Task<string> GenerateAsync(
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
