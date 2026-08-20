namespace CareerConnect.Api.Contracts;

public class CopilotActionResponse
{
    public required string Title { get; init; }
    public required string Detail { get; init; }
    public required string Priority { get; init; }
    public Guid? ApplicationId { get; init; }
}

public class CopilotInsightsResponse
{
    public required string OverallSummary { get; init; }
    public required List<CopilotActionResponse> Actions { get; init; }
}
