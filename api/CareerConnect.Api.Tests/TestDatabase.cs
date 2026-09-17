using CareerConnect.Api.Data;
using CareerConnect.Api.Domain;
using CareerConnect.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CareerConnect.Api.Tests;

/// <summary>
/// SQLite in-memory database scoped to one test — a real relational engine, so
/// FK constraints and cascade/restrict behavior match production, unlike the
/// EF InMemory provider.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    public AppDbContext Db { get; }

    /// <summary>
    /// A real plan service over this database, with no complimentary emails
    /// configured — services under test take the interface, and faking it would
    /// only test the fake.
    /// </summary>
    public IPlanService Plans => new PlanService(Db, new ConfigurationBuilder().Build());

    public TestDatabase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options);
        Db.Database.EnsureCreated();
    }

    /// <summary>
    /// Pro by default: most of what there is to test is the automated tier, and
    /// a test that cares about Free says so.
    /// </summary>
    public Guid SeedUser(string email, PlanTier plan = PlanTier.Pro)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = "not-a-real-hash",
            Plan = plan,
            CreatedAtUtc = DateTime.UtcNow,
        };
        Db.Users.Add(user);
        Db.SaveChanges();
        return user.Id;
    }

    public Application SeedApplication(
        Guid userId,
        string company = "Acme",
        string? jobDescription = "We need a backend engineer with ASP.NET Core experience.",
        ApplicationStatus status = ApplicationStatus.Applied)
    {
        var application = new Application
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CompanyName = company,
            RoleTitle = "Software Engineer",
            Status = status,
            DateApplied = new DateOnly(2026, 8, 1),
            JobDescriptionText = jobDescription,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        Db.Applications.Add(application);
        Db.SaveChanges();
        return application;
    }

    public InterviewEvent SeedInterview(
        Guid applicationId,
        DateTime? scheduledAtUtc = null,
        InterviewKind kind = InterviewKind.PhoneScreen,
        ChangeSource source = ChangeSource.Manual,
        string? calendarEventId = null)
    {
        var interview = new InterviewEvent
        {
            Id = Guid.NewGuid(),
            ApplicationId = applicationId,
            ScheduledAtUtc = scheduledAtUtc ?? new DateTime(2026, 9, 10, 14, 0, 0, DateTimeKind.Utc),
            Kind = kind,
            Source = source,
            CalendarEventId = calendarEventId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        Db.InterviewEvents.Add(interview);
        Db.SaveChanges();
        return interview;
    }

    public Resume SeedResume(Guid userId, string label = "Primary", bool isActive = true, ResumeLayout? layout = null)
    {
        var resume = new Resume
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Label = label,
            Content = layout?.ToPlainText() ?? new string('x', 200),
            Layout = layout,
            IsActive = isActive,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        Db.Resumes.Add(resume);
        Db.SaveChanges();
        return resume;
    }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}
