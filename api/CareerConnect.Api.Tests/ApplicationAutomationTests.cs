using CareerConnect.Api.Domain;
using CareerConnect.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace CareerConnect.Api.Tests;

public sealed class ApplicationAutomationTests : IDisposable
{
    private readonly TestDatabase _fixture = new();
    private readonly FakeInterviewCalendarSync _calendar = new();
    private readonly Guid _userId;

    private static readonly EmailEvidence Evidence =
        new("They said no.", "Update on your application", "talent@acme.com", new DateTime(2026, 9, 1, 15, 0, 0, DateTimeKind.Utc));

    public ApplicationAutomationTests()
    {
        _userId = _fixture.SeedUser("me@example.com");
    }

    public void Dispose() => _fixture.Dispose();

    private ApplicationAutomation Automation(int? ghostAfterDays = null) => new(
        _fixture.Db,
        new InterviewService(_fixture.Db, _calendar),
        new ConfigurationBuilder()
            .AddInMemoryCollection(ghostAfterDays is null ? [] : [new("Automation:GhostAfterDays", ghostAfterDays.ToString())])
            .Build(),
        NullLogger<ApplicationAutomation>.Instance);

    private Task<Application> ReloadAsync(Guid id) =>
        _fixture.Db.Applications.AsNoTracking().Include(a => a.Interviews).FirstAsync(a => a.Id == id);

    [Theory]
    [InlineData(ApplicationStatus.Preparing, ApplicationStatus.Applied, false, true)]
    [InlineData(ApplicationStatus.Applied, ApplicationStatus.Rejected, true, true)]
    [InlineData(ApplicationStatus.Interview, ApplicationStatus.Rejected, true, true)]
    [InlineData(ApplicationStatus.Applied, ApplicationStatus.PhoneScreen, true, true)]
    [InlineData(ApplicationStatus.Ghosted, ApplicationStatus.Interview, true, true)]
    [InlineData(ApplicationStatus.PhoneScreen, ApplicationStatus.Interview, true, true)]
    // Only when the email said so outright.
    [InlineData(ApplicationStatus.Applied, ApplicationStatus.Rejected, false, false)]
    // An offer is always seen before it's recorded.
    [InlineData(ApplicationStatus.Interview, ApplicationStatus.Offer, true, false)]
    // Nothing moves backwards on its own.
    [InlineData(ApplicationStatus.Interview, ApplicationStatus.PhoneScreen, true, false)]
    [InlineData(ApplicationStatus.Offer, ApplicationStatus.Rejected, true, false)]
    // Skipping Applied entirely deserves a look.
    [InlineData(ApplicationStatus.Preparing, ApplicationStatus.Interview, true, false)]
    [InlineData(ApplicationStatus.Preparing, ApplicationStatus.Rejected, true, false)]
    // "Ghosted" is never read off an email.
    [InlineData(ApplicationStatus.Applied, ApplicationStatus.Ghosted, true, false)]
    public void AutoApplyPolicy_OnlyAppliesClearForwardMovesAndRejections(
        ApplicationStatus current, ApplicationStatus suggested, bool clearCut, bool expected)
    {
        Assert.Equal(expected, AutoApplyPolicy.ShouldApply(current, suggested, clearCut));
    }

    [Fact]
    public async Task ApplyFromEmailAsync_ChangesTheStatus_AndRecordsWhatItDid()
    {
        var application = _fixture.SeedApplication(_userId, status: ApplicationStatus.Applied);

        var activity = await Automation().ApplyFromEmailAsync(_userId, application.Id, ApplicationStatus.Rejected, Evidence, null);

        Assert.NotNull(activity);
        Assert.Equal(ApplicationStatus.Rejected, (await ReloadAsync(application.Id)).Status);
        var stored = await _fixture.Db.ActivityEvents.AsNoTracking().SingleAsync();
        Assert.Equal(ApplicationStatus.Applied, stored.FromStatus);
        Assert.Equal("Update on your application", stored.EmailSubject);
        var change = await _fixture.Db.StatusChanges.AsNoTracking().SingleAsync(c => c.ToStatus == ApplicationStatus.Rejected);
        Assert.Equal(ChangeSource.EmailAutomatic, change.Source);
    }

    [Fact]
    public async Task ApplyFromEmailAsync_SchedulesTheInterviewTheEmailNamed()
    {
        var application = _fixture.SeedApplication(_userId, status: ApplicationStatus.Applied);
        var at = new DateTime(2026, 9, 20, 18, 0, 0, DateTimeKind.Utc);

        var activity = await Automation().ApplyFromEmailAsync(
            _userId, application.Id, ApplicationStatus.PhoneScreen, Evidence, (at, InterviewKind.PhoneScreen));

        var interview = Assert.Single((await ReloadAsync(application.Id)).Interviews);
        Assert.Equal(at, interview.ScheduledAtUtc);
        Assert.Equal(interview.Id, activity!.InterviewEventId);
    }

    [Fact]
    public async Task UndoAsync_PutsTheStatusDateAndInterviewBack()
    {
        var application = _fixture.SeedApplication(_userId, status: ApplicationStatus.Preparing);
        var automation = Automation();
        var confirmation = Evidence with { ReceivedAtUtc = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc) };
        var activity = await automation.ApplyFromEmailAsync(_userId, application.Id, ApplicationStatus.Applied, confirmation, null);
        Assert.Equal(new DateOnly(2026, 8, 20), (await ReloadAsync(application.Id)).DateApplied);

        var outcome = await automation.UndoAsync(_userId, activity!.Id);

        var undone = Assert.IsType<UndoOutcome.Undone>(outcome);
        Assert.False(undone.Activity.CanUndo);
        var reloaded = await ReloadAsync(application.Id);
        Assert.Equal(ApplicationStatus.Preparing, reloaded.Status);
        Assert.Equal(new DateOnly(2026, 8, 1), reloaded.DateApplied);
    }

    [Fact]
    public async Task UndoAsync_RemovesTheInterviewItScheduled()
    {
        var application = _fixture.SeedApplication(_userId, status: ApplicationStatus.Applied);
        var automation = Automation();
        var activity = await automation.ApplyFromEmailAsync(
            _userId, application.Id, ApplicationStatus.Interview, Evidence, (DateTime.UtcNow.AddDays(3), InterviewKind.Technical));

        await automation.UndoAsync(_userId, activity!.Id);

        var reloaded = await ReloadAsync(application.Id);
        Assert.Empty(reloaded.Interviews);
        Assert.Equal(ApplicationStatus.Applied, reloaded.Status);
    }

    [Fact]
    public async Task UndoAsync_RefusesOnceTheApplicationHasMovedOn()
    {
        var application = _fixture.SeedApplication(_userId, status: ApplicationStatus.Applied);
        var automation = Automation();
        var activity = await automation.ApplyFromEmailAsync(_userId, application.Id, ApplicationStatus.PhoneScreen, Evidence, null);
        var tracked = await _fixture.Db.Applications.FirstAsync(a => a.Id == application.Id);
        tracked.Status = ApplicationStatus.Interview;
        await _fixture.Db.SaveChangesAsync();

        var outcome = await automation.UndoAsync(_userId, activity!.Id);

        Assert.IsType<UndoOutcome.Conflict>(outcome);
        Assert.Equal(ApplicationStatus.Interview, (await ReloadAsync(application.Id)).Status);
    }

    [Fact]
    public async Task UndoAsync_RefusesTwice_AndIgnoresOtherUsers()
    {
        var application = _fixture.SeedApplication(_userId, status: ApplicationStatus.Applied);
        var automation = Automation();
        var activity = await automation.ApplyFromEmailAsync(_userId, application.Id, ApplicationStatus.Rejected, Evidence, null);
        var otherUser = _fixture.SeedUser("someone@example.com");

        Assert.IsType<UndoOutcome.NotFound>(await automation.UndoAsync(otherUser, activity!.Id));
        Assert.IsType<UndoOutcome.Undone>(await automation.UndoAsync(_userId, activity.Id));
        Assert.IsType<UndoOutcome.Conflict>(await automation.UndoAsync(_userId, activity.Id));
    }

    [Fact]
    public async Task GhostSilentApplicationsAsync_MarksOnlyLongSilentAppliedJobs()
    {
        Application Seed(ApplicationStatus status, int daysSilent)
        {
            var application = _fixture.SeedApplication(_userId, $"Co{Guid.NewGuid():N}", status: status);
            application.UpdatedAtUtc = DateTime.UtcNow.AddDays(-daysSilent);
            _fixture.Db.SaveChanges();
            return application;
        }

        var silent = Seed(ApplicationStatus.Applied, 31);
        var recent = Seed(ApplicationStatus.Applied, 10);
        var interviewing = Seed(ApplicationStatus.Interview, 40);
        var upcoming = Seed(ApplicationStatus.Applied, 45);
        _fixture.SeedInterview(upcoming.Id, scheduledAtUtc: DateTime.UtcNow.AddDays(2));

        var count = await Automation().GhostSilentApplicationsAsync();

        Assert.Equal(1, count);
        Assert.Equal(ApplicationStatus.Ghosted, (await ReloadAsync(silent.Id)).Status);
        Assert.Equal(ApplicationStatus.Applied, (await ReloadAsync(recent.Id)).Status);
        Assert.Equal(ApplicationStatus.Interview, (await ReloadAsync(interviewing.Id)).Status);
        Assert.Equal(ApplicationStatus.Applied, (await ReloadAsync(upcoming.Id)).Status);
        var activity = await _fixture.Db.ActivityEvents.AsNoTracking().SingleAsync();
        Assert.Equal(ActivityTrigger.Inactivity, activity.Trigger);
    }

    [Fact]
    public async Task GhostSilentApplicationsAsync_DoesNothing_WhenTurnedOff()
    {
        var application = _fixture.SeedApplication(_userId, status: ApplicationStatus.Applied);
        application.UpdatedAtUtc = DateTime.UtcNow.AddDays(-90);
        _fixture.Db.SaveChanges();

        Assert.Equal(0, await Automation(ghostAfterDays: 0).GhostSilentApplicationsAsync());
    }

    [Fact]
    public async Task ListAsync_ShowsRecentChangesWithTheirCompany_NewestFirst()
    {
        var first = _fixture.SeedApplication(_userId, "Acme", status: ApplicationStatus.Applied);
        var second = _fixture.SeedApplication(_userId, "Globex", status: ApplicationStatus.Applied);
        var automation = Automation();
        await automation.ApplyFromEmailAsync(_userId, first.Id, ApplicationStatus.Rejected, Evidence, null);
        await automation.ApplyFromEmailAsync(_userId, second.Id, ApplicationStatus.PhoneScreen, Evidence, null);

        var feed = await automation.ListAsync(_userId, days: 14);

        Assert.Equal(["Globex", "Acme"], feed.Select(f => f.CompanyName));
        Assert.All(feed, f => Assert.True(f.CanUndo));
    }
}
