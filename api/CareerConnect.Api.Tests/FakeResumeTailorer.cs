using CareerConnect.Api.Services;

namespace CareerConnect.Api.Tests;

/// <summary>
/// Stand-in for the Claude-backed tailorer so orchestration can be tested
/// without network access or an API key.
/// </summary>
public sealed class FakeResumeTailorer : IResumeTailorer
{
    public bool IsConfigured { get; set; } = true;

    /// <summary>Set to throw instead of returning, to exercise the failure path.</summary>
    public ResumeTailorException? ThrowOnTailor { get; set; }

    public string Result { get; set; } = "Tailored resume text.";

    public int CallCount { get; private set; }
    public string? LastResumeText { get; private set; }
    public string? LastJobDescriptionText { get; private set; }

    public Task<string> TailorAsync(
        string resumeText,
        string jobDescriptionText,
        string roleTitle,
        string companyName,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastResumeText = resumeText;
        LastJobDescriptionText = jobDescriptionText;

        if (ThrowOnTailor is not null)
        {
            throw ThrowOnTailor;
        }

        // Suffixed so a test running several passes can tell which rewrite was kept.
        return Task.FromResult(CallCount == 1 ? Result : $"{Result} (pass {CallCount})");
    }
}
