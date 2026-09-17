namespace CareerConnect.Api.Domain;

public class User
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public string? DisplayName { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// What this account can use. Everyone starts on Free; Pro is granted
    /// either by storing it here or by listing the email in Billing:ProEmails
    /// (see <see cref="Services.PlanService"/>).
    /// </summary>
    public PlanTier Plan { get; set; } = PlanTier.Free;

    /// <summary>When the plan last changed, so an upgrade or downgrade is traceable.</summary>
    public DateTime? PlanChangedAtUtc { get; set; }

    public List<Application> Applications { get; set; } = [];
    public List<Resume> Resumes { get; set; } = [];
    public GmailConnection? GmailConnection { get; set; }
}
