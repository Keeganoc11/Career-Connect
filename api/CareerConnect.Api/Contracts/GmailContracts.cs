using CareerConnect.Api.Domain;

namespace CareerConnect.Api.Contracts;

public class GmailConnectionResponse
{
    public required bool Connected { get; init; }
    public string? ConnectedEmail { get; init; }
    public DateTime? ConnectedAtUtc { get; init; }
    public DateTime? LastCheckedAtUtc { get; init; }
    public bool HasPendingSuggestions { get; init; }
}

public class GmailAuthorizationUrlResponse
{
    public required string AuthorizationUrl { get; init; }
}

public class SuggestedStatusUpdateResponse
{
    public required Guid ApplicationId { get; init; }
    public required string CompanyName { get; init; }
    public required string RoleTitle { get; init; }
    public required ApplicationStatus CurrentStatus { get; init; }
    public required ApplicationStatus SuggestedStatus { get; init; }
    public required string Reasoning { get; init; }
    public required string EmailSubject { get; init; }
    public required string EmailFrom { get; init; }
    public required DateTime EmailReceivedAtUtc { get; init; }
}

public class SuggestedNewApplicationResponse
{
    public required string CompanyName { get; init; }
    public required string RoleTitle { get; init; }
    public required string Reasoning { get; init; }
    public required string EmailSubject { get; init; }
    public required string EmailFrom { get; init; }
    public required DateTime EmailReceivedAtUtc { get; init; }
}

public class GmailScanResponse
{
    public required List<SuggestedStatusUpdateResponse> StatusUpdates { get; init; }
    public required List<SuggestedNewApplicationResponse> NewApplications { get; init; }
}
