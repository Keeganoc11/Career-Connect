using CareerConnect.Api.Data;
using CareerConnect.Api.Domain;
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

    public TestDatabase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options);
        Db.Database.EnsureCreated();
    }

    public Guid SeedUser(string email)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = "not-a-real-hash",
            CreatedAtUtc = DateTime.UtcNow,
        };
        Db.Users.Add(user);
        Db.SaveChanges();
        return user.Id;
    }

    public Application SeedApplication(
        Guid userId,
        string company = "Acme",
        string? jobDescription = "We need a backend engineer with ASP.NET Core experience.")
    {
        var application = new Application
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CompanyName = company,
            RoleTitle = "Software Engineer",
            Status = ApplicationStatus.Applied,
            DateApplied = new DateOnly(2026, 8, 1),
            JobDescriptionText = jobDescription,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        Db.Applications.Add(application);
        Db.SaveChanges();
        return application;
    }

    public Resume SeedResume(Guid userId, string label = "Primary", bool isActive = true)
    {
        var resume = new Resume
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Label = label,
            Content = new string('x', 200),
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
