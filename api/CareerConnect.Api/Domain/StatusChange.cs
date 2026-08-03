namespace CareerConnect.Api.Domain;

/// <summary>Append-only audit of status transitions. FromStatus is null for the
/// row recorded when an application is first created.</summary>
public class StatusChange
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public ApplicationStatus? FromStatus { get; set; }
    public ApplicationStatus ToStatus { get; set; }
    public DateTime ChangedAtUtc { get; set; }
    public StatusChangeSource Source { get; set; }

    public Application Application { get; set; } = null!;
}
