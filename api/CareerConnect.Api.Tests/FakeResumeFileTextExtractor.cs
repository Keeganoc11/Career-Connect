using CareerConnect.Api.Services;

namespace CareerConnect.Api.Tests;

/// <summary>
/// Stand-in for the real PDF/DOCX extractor so upload logic (validation,
/// label defaulting) can be tested without real file bytes.
/// </summary>
public sealed class FakeResumeFileTextExtractor : IResumeFileTextExtractor
{
    /// <summary>Set to null to simulate an unsupported extension.</summary>
    public string? Result { get; set; } =
        "Extracted resume text that is long enough to pass the minimum-length check.";

    public string? Extract(Stream fileStream, string fileName) => Result;
}
