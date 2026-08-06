using CareerConnect.Api.Domain;
using CareerConnect.Api.Services;

namespace CareerConnect.Api.Tests;

/// <summary>
/// Stand-in for the Claude-backed analyzer so scoring logic can be tested
/// without network access or an API key.
/// </summary>
public sealed class FakeResumeMatchAnalyzer : IResumeMatchAnalyzer
{
    public bool IsConfigured { get; set; } = true;

    /// <summary>Set to throw instead of returning, to exercise the failure path.</summary>
    public MatchAnalysisException? ThrowOnAnalyze { get; set; }

    public MatchAnalysis Result { get; set; } = new(
        Score: 72,
        Summary: "Solid overlap on backend fundamentals; missing cloud experience.",
        MatchedKeywords: ["ASP.NET Core", "Entity Framework"],
        MissingKeywords: ["Azure", "Kubernetes"],
        Suggestions:
        [
            new SuggestedEdit
            {
                Section = "Experience — Data Center Ops",
                Guidance = "Your SQL work is buried in a bullet about hardware; pull it forward.",
                SuggestedText = "Wrote and maintained SQL queries for inventory and asset-tracking systems supporting [team/scale].",
            },
        ],
        ModelId: "claude-opus-5");

    public int CallCount { get; private set; }
    public string? LastResumeText { get; private set; }
    public string? LastJobDescriptionText { get; private set; }

    public Task<MatchAnalysis> AnalyzeAsync(
        string resumeText,
        string jobDescriptionText,
        string roleTitle,
        string companyName,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastResumeText = resumeText;
        LastJobDescriptionText = jobDescriptionText;

        if (ThrowOnAnalyze is not null)
        {
            throw ThrowOnAnalyze;
        }

        return Task.FromResult(Result);
    }
}
