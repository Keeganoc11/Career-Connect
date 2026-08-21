using CareerConnect.Api.Domain;
using CareerConnect.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CareerConnect.Api.Tests;

public sealed class ScheduledGmailScanRunnerTests : IDisposable
{
    private readonly TestDatabase _fixture = new();
    private readonly FakeGmailUpdateScanner _scanner = new();
    private readonly ScheduledGmailScanRunner _runner;

    public ScheduledGmailScanRunnerTests()
    {
        _runner = new ScheduledGmailScanRunner(
            _fixture.Db, _scanner, NullLogger<ScheduledGmailScanRunner>.Instance);
    }

    public void Dispose() => _fixture.Dispose();

    private GmailConnection SeedConnection(Guid userId)
    {
        var connection = new GmailConnection
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ConnectedEmail = "me@example.com",
            EncryptedRefreshToken = "not-a-real-token",
            ConnectedAtUtc = DateTime.UtcNow,
        };
        _fixture.Db.GmailConnections.Add(connection);
        _fixture.Db.SaveChanges();
        return connection;
    }

    [Fact]
    public async Task RunAllAsync_PersistsPendingResult_WhenScanFindsSomething()
    {
        var userId = _fixture.SeedUser("me@example.com");
        SeedConnection(userId);
        _scanner.DefaultOutcome = new GmailScanOutcome.Success(
            [new Contracts.SuggestedStatusUpdateResponse
            {
                ApplicationId = Guid.NewGuid(),
                CompanyName = "Acme",
                RoleTitle = "Engineer",
                CurrentStatus = ApplicationStatus.Applied,
                SuggestedStatus = ApplicationStatus.Interview,
                Reasoning = "Got an interview email.",
                EmailSubject = "Interview",
                EmailFrom = "hr@acme.com",
                EmailReceivedAtUtc = DateTime.UtcNow,
            }],
            []);

        await _runner.RunAllAsync();

        var connection = await _fixture.Db.GmailConnections.AsNoTracking().FirstAsync(g => g.UserId == userId);
        Assert.NotNull(connection.PendingScanResultJson);
        Assert.Contains("Acme", connection.PendingScanResultJson);
        Assert.NotNull(connection.PendingScanCompletedAtUtc);
    }

    [Fact]
    public async Task RunAllAsync_LeavesPendingResultNull_WhenScanFindsNothing()
    {
        var userId = _fixture.SeedUser("me@example.com");
        SeedConnection(userId);
        _scanner.DefaultOutcome = new GmailScanOutcome.Success([], []);

        await _runner.RunAllAsync();

        var connection = await _fixture.Db.GmailConnections.AsNoTracking().FirstAsync(g => g.UserId == userId);
        Assert.Null(connection.PendingScanResultJson);
    }

    [Fact]
    public async Task RunAllAsync_SkipsGracefully_WhenScanFails()
    {
        var userId = _fixture.SeedUser("me@example.com");
        SeedConnection(userId);
        _scanner.DefaultOutcome = new GmailScanOutcome.Failed("No Anthropic key configured.");

        await _runner.RunAllAsync();

        var connection = await _fixture.Db.GmailConnections.AsNoTracking().FirstAsync(g => g.UserId == userId);
        Assert.Null(connection.PendingScanResultJson);
    }

    [Fact]
    public async Task RunAllAsync_ScansEveryConnectedUser()
    {
        var user1 = _fixture.SeedUser("one@example.com");
        var user2 = _fixture.SeedUser("two@example.com");
        SeedConnection(user1);
        SeedConnection(user2);

        await _runner.RunAllAsync();

        Assert.Equal(2, _scanner.ScannedUserIds.Count);
        Assert.Contains(user1, _scanner.ScannedUserIds);
        Assert.Contains(user2, _scanner.ScannedUserIds);
    }

    [Fact]
    public async Task RunAllAsync_OverwritesPreviousPendingResult()
    {
        var userId = _fixture.SeedUser("me@example.com");
        var connection = SeedConnection(userId);
        connection.PendingScanResultJson = """{"statusUpdates":[],"newApplications":[{"companyName":"Old","roleTitle":"x","reasoning":"x","emailSubject":"x","emailFrom":"x","emailReceivedAtUtc":"2026-01-01T00:00:00Z"}]}""";
        _fixture.Db.SaveChanges();

        _scanner.DefaultOutcome = new GmailScanOutcome.Success(
            [],
            [new Contracts.SuggestedNewApplicationResponse
            {
                CompanyName = "New",
                RoleTitle = "Engineer",
                Reasoning = "Confirmation email.",
                EmailSubject = "Thanks for applying",
                EmailFrom = "hr@new.com",
                EmailReceivedAtUtc = DateTime.UtcNow,
            }]);

        await _runner.RunAllAsync();

        var updated = await _fixture.Db.GmailConnections.AsNoTracking().FirstAsync(g => g.UserId == userId);
        Assert.Contains("New", updated.PendingScanResultJson);
        Assert.DoesNotContain("Old", updated.PendingScanResultJson);
    }
}
