using CareerConnect.Api.Services;

namespace CareerConnect.Api.Tests;

/// <summary>
/// Stand-in for the Claude-backed analyzer so pipeline orchestration can be
/// tested without network access or an API key.
/// </summary>
public sealed class FakeCopilotAnalyzer : ICopilotAnalyzer
{
    public bool IsConfigured { get; set; } = true;

    /// <summary>Set to throw instead of returning, to exercise the failure path.</summary>
    public CopilotAnalysisException? ThrowOnAnalyze { get; set; }

    public CopilotInsights Result { get; set; } = new(
        "You have one application in flight.",
        [new CopilotAction("Follow up with Acme", "No response in 3 weeks.", "high", 0)]);

    public CopilotPipelineSnapshot? LastSnapshot { get; private set; }

    public Task<CopilotInsights> AnalyzeAsync(
        CopilotPipelineSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        LastSnapshot = snapshot;

        if (ThrowOnAnalyze is not null)
        {
            throw ThrowOnAnalyze;
        }

        return Task.FromResult(Result);
    }
}
