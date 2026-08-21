namespace CareerConnect.Api.Contracts;

public class CoverLetterResponse
{
    public required string Content { get; init; }
}

public class InterviewQuestionResponse
{
    public required string Question { get; init; }
    public required string WhyItMightComeUp { get; init; }
}

public class TalkingPointResponse
{
    public required string Point { get; init; }
    public required string HowToUseIt { get; init; }
}

public class InterviewPrepResponse
{
    public required List<InterviewQuestionResponse> Questions { get; init; }
    public required List<TalkingPointResponse> TalkingPoints { get; init; }
}
