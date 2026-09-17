using CareerConnect.Api.Contracts;
using CareerConnect.Api.Domain;
using CareerConnect.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CareerConnect.Api.Tests;

public sealed class GmailPendingUpdatesTests : IDisposable
{
    private static readonly DateTime Earlier = new(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = new(2026, 9, 3, 9, 0, 0, DateTimeKind.Utc);

    private readonly TestDatabase _fixture = new();
    private readonly GmailPendingUpdates _pending;
    private readonly Guid _userId;

    public GmailPendingUpdatesTests()
    {
        _userId = _fixture.SeedUser("me@example.com");
        _fixture.Db.GmailConnections.Add(new GmailConnection
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            ConnectedEmail = "me@example.com",
            EncryptedRefreshToken = "not-a-real-token",
            ConnectedAtUtc = DateTime.UtcNow,
        });
        _fixture.Db.SaveChanges();
        _pending = new GmailPendingUpdates(_fixture.Db, NullLogger<GmailPendingUpdates>.Instance);
    }

    public void Dispose() => _fixture.Dispose();

    private static SuggestedStatusUpdateResponse StatusUpdate(
        Application application,
        ApplicationStatus suggested = ApplicationStatus.Interview,
        DateTime? receivedAtUtc = null,
        string subject = "Interview invitation") => new()
    {
        ApplicationId = application.Id,
        CompanyName = application.CompanyName,
        RoleTitle = application.RoleTitle,
        CurrentStatus = application.Status,
        SuggestedStatus = suggested,
        Reasoning = "The email says so.",
        EmailSubject = subject,
        EmailFrom = "recruiter@example.com",
        EmailReceivedAtUtc = receivedAtUtc ?? Earlier,
    };

    private static SuggestedNewApplicationResponse NewApplication(string company, DateTime? receivedAtUtc = null) => new()
    {
        CompanyName = company,
        RoleTitle = "Engineer",
        Reasoning = "Confirmation email.",
        EmailSubject = "Thanks for applying",
        EmailFrom = "jobs@example.com",
        EmailReceivedAtUtc = receivedAtUtc ?? Earlier,
    };

    private static GmailScanResponse Found(
        SuggestedStatusUpdateResponse[]? statusUpdates = null,
        SuggestedNewApplicationResponse[]? newApplications = null,
        AutoAppliedResponse[]? autoApplied = null) => new()
    {
        StatusUpdates = [.. statusUpdates ?? []],
        NewApplications = [.. newApplications ?? []],
        AutoApplied = [.. autoApplied ?? []],
    };

    private string? StoredJson() => _fixture.Db.GmailConnections.AsNoTracking().Single().PendingScanResultJson;

    [Fact]
    public async Task ReadAsync_ReturnsWhatAScanFound()
    {
        var application = _fixture.SeedApplication(_userId, "Acme");
        await _pending.AddAsync(_userId, Found(statusUpdates: [StatusUpdate(application)]));

        var result = await _pending.ReadAsync(_userId);

        var update = Assert.Single(Assert.IsType<GmailScanResponse>(result).StatusUpdates);
        Assert.Equal(ApplicationStatus.Interview, update.SuggestedStatus);
    }

    [Fact]
    public async Task ReadAsync_DoesNotClearUpdatesThatStillNeedADecision()
    {
        var application = _fixture.SeedApplication(_userId, "Acme");
        await _pending.AddAsync(_userId, Found(
            statusUpdates: [StatusUpdate(application)],
            newApplications: [NewApplication("Globex")]));

        await _pending.ReadAsync(_userId);
        var secondRead = await _pending.ReadAsync(_userId);

        Assert.NotNull(secondRead);
        Assert.Single(secondRead.StatusUpdates);
        Assert.Single(secondRead.NewApplications);
    }

    [Fact]
    public async Task ReadAsync_ShowsAutoAppliedConfirmationsOnce()
    {
        var application = _fixture.SeedApplication(_userId, "Acme", status: ApplicationStatus.Applied);
        await _pending.AddAsync(_userId, Found(autoApplied:
        [
            new AutoAppliedResponse
            {
                ApplicationId = application.Id,
                CompanyName = "Acme",
                RoleTitle = "Software Engineer",
                Reasoning = "Confirmation email.",
                EmailSubject = "We received your application",
                EmailFrom = "jobs@acme.com",
                EmailReceivedAtUtc = Earlier,
            },
        ]));

        var firstRead = await _pending.ReadAsync(_userId);
        var secondRead = await _pending.ReadAsync(_userId);

        Assert.Single(Assert.IsType<GmailScanResponse>(firstRead).AutoApplied);
        Assert.Null(secondRead);
        Assert.Null(StoredJson());
    }

    [Fact]
    public async Task AddAsync_MergesWithUpdatesAlreadyWaiting()
    {
        var acme = _fixture.SeedApplication(_userId, "Acme");
        await _pending.AddAsync(_userId, Found(statusUpdates: [StatusUpdate(acme)]));
        await _pending.AddAsync(_userId, Found(newApplications: [NewApplication("Globex")]));

        var result = await _pending.ReadAsync(_userId);

        Assert.NotNull(result);
        Assert.Single(result.StatusUpdates);
        Assert.Single(result.NewApplications);
    }

    [Fact]
    public async Task AddAsync_KeepsOnlyTheNewestEmailAboutTheSameStatusChange()
    {
        var application = _fixture.SeedApplication(_userId, "Acme");
        await _pending.AddAsync(_userId, Found(statusUpdates:
            [StatusUpdate(application, receivedAtUtc: Earlier, subject: "Let's find a time")]));
        await _pending.AddAsync(_userId, Found(statusUpdates:
            [StatusUpdate(application, receivedAtUtc: Later, subject: "Rescheduled to Thursday")]));

        var result = await _pending.ReadAsync(_userId);

        var update = Assert.Single(Assert.IsType<GmailScanResponse>(result).StatusUpdates);
        Assert.Equal("Rescheduled to Thursday", update.EmailSubject);
    }

    [Fact]
    public async Task AddAsync_KeepsDifferentSuggestionsForTheSameApplicationApart()
    {
        var application = _fixture.SeedApplication(_userId, "Acme");
        await _pending.AddAsync(_userId, Found(statusUpdates:
        [
            StatusUpdate(application, ApplicationStatus.PhoneScreen),
            StatusUpdate(application, ApplicationStatus.Interview),
        ]));

        var result = await _pending.ReadAsync(_userId);

        Assert.Equal(2, Assert.IsType<GmailScanResponse>(result).StatusUpdates.Count);
    }

    [Fact]
    public async Task AddAsync_TreatsTheSameCompanyInDifferentCaseAsOneSuggestion()
    {
        await _pending.AddAsync(_userId, Found(newApplications: [NewApplication("Globex", Earlier)]));
        await _pending.AddAsync(_userId, Found(newApplications: [NewApplication(" GLOBEX ", Later)]));

        var result = await _pending.ReadAsync(_userId);

        Assert.Single(Assert.IsType<GmailScanResponse>(result).NewApplications);
    }

    [Fact]
    public async Task ReadAsync_DropsUpdatesTheUserAlreadySettledSomeOtherWay()
    {
        var alreadyInterviewing = _fixture.SeedApplication(_userId, "Already Co", status: ApplicationStatus.Applied);
        var deleted = _fixture.SeedApplication(_userId, "Deleted Co");
        var rejected = _fixture.SeedApplication(_userId, "Rejected Co");
        await _pending.AddAsync(_userId, Found(
            statusUpdates:
            [
                StatusUpdate(alreadyInterviewing),
                StatusUpdate(deleted),
                StatusUpdate(rejected),
            ],
            newApplications: [NewApplication("Tracked Now")]));

        alreadyInterviewing.Status = ApplicationStatus.Interview; // changed by hand
        rejected.Status = ApplicationStatus.Rejected;
        _fixture.Db.Applications.Remove(deleted);
        _fixture.SeedApplication(_userId, "tracked now"); // added by hand, different case
        _fixture.Db.SaveChanges();

        Assert.Null(await _pending.ReadAsync(_userId));
        Assert.Null(StoredJson());
    }

    [Fact]
    public async Task ReadAsync_ShowsTheApplicationAsItIsNow()
    {
        var application = _fixture.SeedApplication(_userId, "Acme", status: ApplicationStatus.Applied);
        await _pending.AddAsync(_userId, Found(statusUpdates: [StatusUpdate(application)]));

        application.Status = ApplicationStatus.PhoneScreen;
        application.CompanyName = "Acme Corp";
        _fixture.Db.SaveChanges();

        var update = Assert.Single(Assert.IsType<GmailScanResponse>(await _pending.ReadAsync(_userId)).StatusUpdates);
        Assert.Equal(ApplicationStatus.PhoneScreen, update.CurrentStatus);
        Assert.Equal("Acme Corp", update.CompanyName);
    }

    [Fact]
    public async Task RemoveStatusUpdateAsync_RemovesOnlyThatSuggestion()
    {
        var application = _fixture.SeedApplication(_userId, "Acme");
        await _pending.AddAsync(_userId, Found(
            statusUpdates:
            [
                StatusUpdate(application, ApplicationStatus.PhoneScreen),
                StatusUpdate(application, ApplicationStatus.Interview),
            ],
            newApplications: [NewApplication("Globex")]));

        await _pending.RemoveStatusUpdateAsync(_userId, application.Id, ApplicationStatus.Interview);

        var result = Assert.IsType<GmailScanResponse>(await _pending.ReadAsync(_userId));
        Assert.Equal(ApplicationStatus.PhoneScreen, Assert.Single(result.StatusUpdates).SuggestedStatus);
        Assert.Single(result.NewApplications);
    }

    [Fact]
    public async Task RemoveNewApplicationAsync_MatchesTheCompanyRegardlessOfCase()
    {
        await _pending.AddAsync(_userId, Found(newApplications: [NewApplication("Globex"), NewApplication("Initech")]));

        await _pending.RemoveNewApplicationAsync(_userId, "  globex");

        var result = Assert.IsType<GmailScanResponse>(await _pending.ReadAsync(_userId));
        Assert.Equal("Initech", Assert.Single(result.NewApplications).CompanyName);
    }

    [Fact]
    public async Task RemovingTheLastSuggestion_ClearsTheStoredValue()
    {
        // HasPendingSuggestions is "stored value isn't null", so an empty list
        // left behind would keep reporting updates that aren't there.
        await _pending.AddAsync(_userId, Found(newApplications: [NewApplication("Globex")]));

        await _pending.RemoveNewApplicationAsync(_userId, "Globex");

        Assert.Null(StoredJson());
        Assert.Null(_fixture.Db.GmailConnections.AsNoTracking().Single().PendingScanCompletedAtUtc);
    }

    [Fact]
    public async Task ReadAsync_ReadsRowsWrittenInTheOlderFormat()
    {
        // Written before this service existed: PascalCase, enums as numbers,
        // and no AutoApplied list at all.
        var application = _fixture.SeedApplication(_userId, "Acme", status: ApplicationStatus.Applied);
        var connection = _fixture.Db.GmailConnections.Single();
        connection.PendingScanResultJson =
            $$"""{"StatusUpdates":[{"ApplicationId":"{{application.Id}}","CompanyName":"Acme","RoleTitle":"Software Engineer","CurrentStatus":1,"SuggestedStatus":3,"Reasoning":"x","EmailSubject":"Interview","EmailFrom":"hr@acme.com","EmailReceivedAtUtc":"2026-08-20T12:00:00Z"}],"NewApplications":[]}""";
        _fixture.Db.SaveChanges();

        var update = Assert.Single(Assert.IsType<GmailScanResponse>(await _pending.ReadAsync(_userId)).StatusUpdates);
        Assert.Equal(ApplicationStatus.Interview, update.SuggestedStatus);
    }

    [Fact]
    public async Task ReadAsync_DiscardsAnUnreadableRowInsteadOfFailing()
    {
        var connection = _fixture.Db.GmailConnections.Single();
        connection.PendingScanResultJson = "{ this is not json";
        _fixture.Db.SaveChanges();

        Assert.Null(await _pending.ReadAsync(_userId));
        Assert.Null(StoredJson());
    }

    [Fact]
    public async Task AddAsync_DoesNothing_WhenGmailWasDisconnectedMidScan()
    {
        _fixture.Db.GmailConnections.RemoveRange(_fixture.Db.GmailConnections);
        _fixture.Db.SaveChanges();

        await _pending.AddAsync(_userId, Found(newApplications: [NewApplication("Globex")]));

        Assert.Empty(await _fixture.Db.GmailConnections.ToListAsync());
    }

    [Fact]
    public async Task AddAsync_KeepsOneSuggestionPerJob_HoweverTheCompanyIsWritten()
    {
        await _pending.AddAsync(_userId, Found(newApplications:
        [
            NewApplication("Delta Dental of Missouri", Earlier),
            NewApplication("Delta Dental MO", Later),
            NewApplication("Delta Dental", Earlier),
            NewApplication("Walmart", Earlier),
        ]));

        var read = await _pending.ReadAsync(_userId);

        Assert.Equal(["Delta Dental MO", "Walmart"], read!.NewApplications.Select(n => n.CompanyName).OrderBy(n => n));
    }

    [Fact]
    public async Task RemoveNewApplicationAsync_DismissesEverySpellingOfTheCompany()
    {
        await _pending.AddAsync(_userId, Found(newApplications:
        [
            NewApplication("Delta Dental of Missouri", Earlier),
            NewApplication("Walmart", Earlier),
        ]));

        await _pending.RemoveNewApplicationAsync(_userId, "Delta Dental MO");

        var read = await _pending.ReadAsync(_userId);
        Assert.Equal("Walmart", Assert.Single(read!.NewApplications).CompanyName);
    }
}
