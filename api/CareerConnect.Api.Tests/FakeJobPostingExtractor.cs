using CareerConnect.Api.Services;

namespace CareerConnect.Api.Tests;

/// <summary>
/// Stand-in for the Claude-backed extractor so ingest orchestration can be
/// tested without network access or an API key.
/// </summary>
public sealed class FakeJobPostingExtractor : IJobPostingExtractor
{
    public bool IsConfigured { get; set; } = true;

    /// <summary>Set to throw instead of returning, to exercise the failure path.</summary>
    public JobPostingExtractionException? ThrowOnExtract { get; set; }

    public JobPostingExtraction Result { get; set; } =
        new(IsJobPosting: true, CompanyName: "Acme", RoleTitle: "Senior Backend Engineer", JobDescriptionText: "We need someone with ASP.NET Core experience.");

    public string? LastPageText { get; private set; }

    public Task<JobPostingExtraction> ExtractAsync(string pageText, CancellationToken cancellationToken = default)
    {
        LastPageText = pageText;

        if (ThrowOnExtract is not null)
        {
            throw ThrowOnExtract;
        }

        return Task.FromResult(Result);
    }
}
