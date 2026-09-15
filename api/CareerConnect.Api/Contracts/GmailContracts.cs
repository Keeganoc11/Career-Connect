using CareerConnect.Api.Domain;

namespace CareerConnect.Api.Contracts;

public class GmailConnectionResponse
{
    public required bool Connected { get; init; }
    public string? ConnectedEmail { get; init; }
    public DateTime? ConnectedAtUtc { get; init; }
    public DateTime? LastCheckedAtUtc { get; init; }
    public bool HasPendingSuggestions { get; init; }

    /// <summary>False on connections made before calendar sync existed — drives a "reconnect" prompt.</summary>
    public bool CalendarEnabled { get; init; }
}

public class GmailAuthorizationUrlResponse
{
    public required string AuthorizationUrl { get; init; }
}

/// <summary>
/// Accepting a status change that email scanning suggested. A dedicated
/// endpoint rather than a flag on the normal status PATCH so the provenance
/// stamped into history is decided by the server, not claimed by the caller.
/// </summary>
public class AcceptSuggestionRequest
{
    public required Guid ApplicationId { get; init; }
    public required ApplicationStatus Status { get; init; }

    /// <summary>
    /// When the scan read a time out of the email, accepting also schedules the
    /// interview. Client-supplied because the user can correct it first — a
    /// misread time books the wrong appointment, and they're the authority on
    /// their own calendar.
    /// </summary>
    public DateTime? InterviewAtUtc { get; init; }

    public InterviewKind? InterviewKind { get; init; }
}

/// <summary>A suggested status change is identified by its application and the status it suggests.</summary>
public class DismissStatusUpdateRequest
{
    public required Guid ApplicationId { get; init; }
    public required ApplicationStatus SuggestedStatus { get; init; }
}

/// <summary>A suggested new application is identified by its company.</summary>
public class DismissNewApplicationRequest
{
    public required string CompanyName { get; init; }
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

    /// <summary>The interview time the email named, if it named one. Null is the common case.</summary>
    public DateTime? InterviewAtUtc { get; init; }

    public InterviewKind? InterviewKind { get; init; }
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

/// <summary>An application moved Preparing → Applied without asking, off a confirmation email.</summary>
public class AutoAppliedResponse
{
    public required Guid ApplicationId { get; init; }
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
    public required List<AutoAppliedResponse> AutoApplied { get; init; }
}
