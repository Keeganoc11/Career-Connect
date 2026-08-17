namespace CareerConnect.Api.Domain;

public class User
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public string? DisplayName { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public List<Application> Applications { get; set; } = [];
    public List<Resume> Resumes { get; set; } = [];
    public GmailConnection? GmailConnection { get; set; }
}
