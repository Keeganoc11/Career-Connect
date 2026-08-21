namespace CareerConnect.Api.Services;

public record InterviewQuestion(string Question, string WhyItMightComeUp);

public record TalkingPoint(string Point, string HowToUseIt);

public record InterviewPrep(List<InterviewQuestion> Questions, List<TalkingPoint> TalkingPoints);

/// <summary>
/// Generates likely interview questions and resume-grounded talking points
/// for a specific application. Isolated behind an interface so
/// orchestration is unit-testable without a network call.
/// </summary>
public interface IInterviewPrepGenerator
{
    /// <summary>False when no API key is configured.</summary>
    bool IsConfigured { get; }

    Task<InterviewPrep> GenerateAsync(
        string resumeText,
        string jobDescriptionText,
        string roleTitle,
        string companyName,
        CancellationToken cancellationToken = default);
}

/// <summary>Thrown when the model call fails or returns something unusable.</summary>
public class InterviewPrepGenerationException(string message, Exception? inner = null)
    : Exception(message, inner);
