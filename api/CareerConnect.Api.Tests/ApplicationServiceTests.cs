using CareerConnect.Api.Contracts;
using CareerConnect.Api.Data;
using CareerConnect.Api.Domain;
using CareerConnect.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CareerConnect.Api.Tests;

/// <summary>
/// Service-layer tests against SQLite in-memory — a real relational engine, so
/// FK constraints and cascade deletes behave like production, unlike the
/// EF InMemory provider.
/// </summary>
public sealed class ApplicationServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly ApplicationService _service;
    private readonly Guid _userId;
    private readonly Guid _otherUserId;

    public ApplicationServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        _userId = SeedUser("me@example.com");
        _otherUserId = SeedUser("someone-else@example.com");
        _service = new ApplicationService(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private Guid SeedUser(string email)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = "not-a-real-hash",
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.Users.Add(user);
        _db.SaveChanges();
        return user.Id;
    }

    private static CreateApplicationRequest NewRequest(
        string company = "Acme",
        ApplicationStatus status = ApplicationStatus.Applied) => new()
    {
        CompanyName = company,
        RoleTitle = "Software Engineer",
        Status = status,
        DateApplied = new DateOnly(2026, 8, 1)
    };

    [Fact]
    public async Task CreateAsync_RecordsInitialStatusHistory()
    {
        var created = await _service.CreateAsync(_userId, NewRequest());

        var history = Assert.IsType<List<StatusChangeResponse>>(created.StatusHistory);
        var entry = Assert.Single(history);
        Assert.Null(entry.FromStatus);
        Assert.Equal(ApplicationStatus.Applied, entry.ToStatus);
        Assert.Equal(StatusChangeSource.Manual, entry.Source);
    }

    [Fact]
    public async Task ListAsync_ReturnsOnlyOwnApplications()
    {
        await _service.CreateAsync(_userId, NewRequest("Mine"));
        await _service.CreateAsync(_otherUserId, NewRequest("Theirs"));

        var list = await _service.ListAsync(_userId);

        var only = Assert.Single(list);
        Assert.Equal("Mine", only.CompanyName);
    }

    [Fact]
    public async Task UpdateStatusAsync_ChangesStatusAndAppendsHistory()
    {
        var created = await _service.CreateAsync(_userId, NewRequest());

        var updated = await _service.UpdateStatusAsync(
            _userId, created.Id, ApplicationStatus.PhoneScreen);

        Assert.NotNull(updated);
        Assert.Equal(ApplicationStatus.PhoneScreen, updated.Status);
        Assert.NotNull(updated.StatusHistory);
        Assert.Equal(2, updated.StatusHistory.Count);
        var latest = updated.StatusHistory[^1];
        Assert.Equal(ApplicationStatus.Applied, latest.FromStatus);
        Assert.Equal(ApplicationStatus.PhoneScreen, latest.ToStatus);
    }

    [Fact]
    public async Task UpdateStatusAsync_SameStatus_DoesNotAppendHistory()
    {
        var created = await _service.CreateAsync(_userId, NewRequest());

        var updated = await _service.UpdateStatusAsync(
            _userId, created.Id, ApplicationStatus.Applied);

        Assert.NotNull(updated);
        Assert.NotNull(updated.StatusHistory);
        Assert.Single(updated.StatusHistory);
    }

    [Fact]
    public async Task UpdateAsync_DoesNotChangeStatus()
    {
        var created = await _service.CreateAsync(
            _userId, NewRequest(status: ApplicationStatus.Interview));

        var updated = await _service.UpdateAsync(_userId, created.Id, new UpdateApplicationRequest
        {
            CompanyName = "Acme Corp",
            RoleTitle = "Senior Software Engineer",
            DateApplied = new DateOnly(2026, 8, 2)
        });

        Assert.NotNull(updated);
        Assert.Equal("Acme Corp", updated.CompanyName);
        Assert.Equal(ApplicationStatus.Interview, updated.Status);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsNull_ForAnotherUsersApplication()
    {
        var created = await _service.CreateAsync(_otherUserId, NewRequest());

        var updated = await _service.UpdateAsync(_userId, created.Id, new UpdateApplicationRequest
        {
            CompanyName = "Hijacked",
            RoleTitle = "Nope",
            DateApplied = new DateOnly(2026, 8, 1)
        });

        Assert.Null(updated);
    }

    [Fact]
    public async Task DeleteAsync_CascadesStatusHistory()
    {
        var created = await _service.CreateAsync(_userId, NewRequest());
        await _service.UpdateStatusAsync(_userId, created.Id, ApplicationStatus.Rejected);

        var deleted = await _service.DeleteAsync(_userId, created.Id);

        Assert.True(deleted);
        Assert.Empty(await _db.Applications.ToListAsync());
        Assert.Empty(await _db.StatusChanges.ToListAsync());
    }

    [Fact]
    public async Task GetSummaryAsync_CountsPerStatus()
    {
        await _service.CreateAsync(_userId, NewRequest("A"));
        await _service.CreateAsync(_userId, NewRequest("B"));
        await _service.CreateAsync(_userId, NewRequest("C", ApplicationStatus.Offer));
        await _service.CreateAsync(_otherUserId, NewRequest("NotMine"));

        var summary = await _service.GetSummaryAsync(_userId);

        Assert.Equal(3, summary.Total);
        Assert.Equal(Enum.GetValues<ApplicationStatus>().Length, summary.Counts.Count);
        Assert.Equal(2, summary.Counts.Single(c => c.Status == ApplicationStatus.Applied).Count);
        Assert.Equal(1, summary.Counts.Single(c => c.Status == ApplicationStatus.Offer).Count);
        Assert.Equal(0, summary.Counts.Single(c => c.Status == ApplicationStatus.Ghosted).Count);
    }
}
