namespace CareerConnect.Api.Domain;

public enum ApplicationStatus
{
    /// <summary>Found the posting, haven't applied yet — the automated prep pipeline runs here.</summary>
    Preparing,
    Applied,
    PhoneScreen,
    Interview,
    Offer,
    Rejected,
    Ghosted,
    Withdrawn
}
