using System.Text.Json;
using CareerConnect.Api.Domain;
using CareerConnect.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CareerConnect.Api.Tests;

public sealed class AccountServiceTests : IDisposable
{
    private readonly TestDatabase _fixture = new();
    private readonly FakeGmailOAuthService _gmail = new();
    private readonly AccountService _service;
    private readonly Guid _userId;

    public AccountServiceTests()
    {
        _userId = _fixture.SeedUser("me@example.com");
        _service = new AccountService(_fixture.Db, _gmail, NullLogger<AccountService>.Instance);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task ExportAsync_IncludesEverythingTheAccountHolds()
    {
        var application = _fixture.SeedApplication(_userId, "Northwind");
        var interview = _fixture.SeedInterview(application.Id);
        interview.ResearchNotes = "They rewrote billing last quarter.";
        _fixture.Db.InterviewQuestions.Add(new InterviewQuestionEntry
        {
            Id = Guid.NewGuid(),
            InterviewEventId = interview.Id,
            Side = InterviewQuestionSide.TheyAsked,
            Kind = InterviewQuestionKind.Technical,
            Text = "How would you debug a deadlock?",
            Answer = "Read pg_locks first.",
            CreatedAtUtc = DateTime.UtcNow,
        });
        _fixture.SeedResume(_userId, "Main");
        await _fixture.Db.SaveChangesAsync();

        var export = await _service.ExportAsync(_userId);

        // Serialized, because that's the only form the user ever sees.
        var json = JsonSerializer.Serialize(export);
        Assert.Contains("Northwind", json);
        Assert.Contains("They rewrote billing last quarter.", json);
        Assert.Contains("How would you debug a deadlock?", json);
        Assert.Contains("Read pg_locks first.", json);
        Assert.Contains("me@example.com", json);
    }

    [Fact]
    public async Task ExportAsync_NeverIncludesThePasswordHash()
    {
        var json = JsonSerializer.Serialize(await _service.ExportAsync(_userId));

        var hash = await _fixture.Db.Users.AsNoTracking()
            .Where(u => u.Id == _userId).Select(u => u.PasswordHash).SingleAsync();
        Assert.DoesNotContain(hash, json);
        Assert.DoesNotContain("PasswordHash", json);
    }

    [Fact]
    public async Task ExportAsync_LeavesOutOtherPeoplesData()
    {
        var someoneElse = _fixture.SeedUser("other@example.com");
        _fixture.SeedApplication(someoneElse, "NotYours");

        var json = JsonSerializer.Serialize(await _service.ExportAsync(_userId));

        Assert.DoesNotContain("NotYours", json);
    }

    [Fact]
    public async Task ExportAsync_ReturnsNothingForAnAccountThatIsGone() =>
        Assert.Null(await _service.ExportAsync(Guid.NewGuid()));

    [Fact]
    public async Task DeleteAsync_RemovesTheAccountAndEverythingUnderIt()
    {
        var application = _fixture.SeedApplication(_userId);
        _fixture.SeedInterview(application.Id);
        _fixture.SeedResume(_userId);

        var outcome = await _service.DeleteAsync(_userId, "me@example.com");

        Assert.Equal(DeleteAccountOutcome.Deleted, outcome);
        Assert.Empty(await _fixture.Db.Users.ToListAsync());
        Assert.Empty(await _fixture.Db.Applications.ToListAsync());
        Assert.Empty(await _fixture.Db.InterviewEvents.ToListAsync());
        Assert.Empty(await _fixture.Db.Resumes.ToListAsync());
    }

    [Fact]
    public async Task DeleteAsync_HandsBackTheGoogleGrantFirst()
    {
        await _service.DeleteAsync(_userId, "me@example.com");

        // Once the row is gone there's no stored token left to revoke with, so
        // the order matters, not just that it happened.
        Assert.Contains(_userId, _gmail.DisconnectedUserIds);
    }

    [Fact]
    public async Task DeleteAsync_RefusesWhenTheTypedEmailDoesNotMatch()
    {
        var outcome = await _service.DeleteAsync(_userId, "someone.else@example.com");

        Assert.Equal(DeleteAccountOutcome.ConfirmationMismatch, outcome);
        Assert.Single(await _fixture.Db.Users.ToListAsync());
    }

    [Fact]
    public async Task DeleteAsync_IgnoresCaseAndSpacingInTheConfirmation()
    {
        var outcome = await _service.DeleteAsync(_userId, "  Me@Example.com ");

        Assert.Equal(DeleteAccountOutcome.Deleted, outcome);
    }

    [Fact]
    public async Task DeleteAsync_LeavesOtherAccountsAlone()
    {
        var someoneElse = _fixture.SeedUser("other@example.com");
        _fixture.SeedApplication(someoneElse, "TheirJob");

        await _service.DeleteAsync(_userId, "me@example.com");

        Assert.Single(await _fixture.Db.Users.ToListAsync());
        Assert.Single(await _fixture.Db.Applications.ToListAsync());
    }
}
