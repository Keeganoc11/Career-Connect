namespace CareerConnect.Api.Services;

/// <summary>Pulls plain text out of an uploaded resume file (PDF or DOCX).</summary>
public interface IResumeFileTextExtractor
{
    /// <summary>Returns null when the file extension isn't supported.</summary>
    string? Extract(Stream fileStream, string fileName);
}
