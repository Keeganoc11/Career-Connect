using CareerConnect.Api.Contracts;
using CareerConnect.Api.Domain;
using CareerConnect.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace CareerConnect.Api.Tests;

public sealed class InterviewServiceTests : IDisposable
{
    private static readonly DateTime Slot = new(2026, 9, 10, 14, 0, 0, DateTimeKind.Utc);

    private readonly TestDatabase _fixture = new();
    private readonly FakeInterviewCalendarSync _calendar = new();
    private readonly InterviewService _service;
    private readonly Guid _userId;
    private readonly Guid _otherUserId;

    public InterviewServiceTests()
    {
        _userId = _fixture.SeedUser("me@example.com");
        _otherUserId = _fixture.SeedUser("someone-else@example.com");
        _service = new InterviewService(_fixture.Db, _calendar);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task CreateAsync_SchedulesTheInterviewAndSyncsItToTheCalendar()
    {
        var application = _fixture.SeedApplication(_userId);

        var created = await _service.CreateAsync(_userId, application.Id, new CreateInterviewRequest
        {
            ScheduledAtUtc = Slot,
            Kind = InterviewKind.Technical,
            Notes = "  Zoom link in the invite  ",
        });

        Assert.NotNull(created);
        Assert.Equal(Slot, created.ScheduledAtUtc);
        Assert.Equal(InterviewKind.Technical, created.Kind);
        Assert.Equal("Zoom link in the invite", created.Notes);
        Assert.Equal(ChangeSource.Manual, created.Source);
        Assert.True(created.OnCalendar);

        var stored = await _fixture.Db.InterviewEvents.AsNoTracking().SingleAsync();
        Assert.Equal("cal-event-1", stored.CalendarEventId);
        Assert.Equal([stored.Id], _calendar.UpsertedInterviewIds);
    }

    [Fact]
    public async Task CreateAsync_StillSavesTheInterview_WhenCalendarSyncIsOff()
    {
        var application = _fixture.SeedApplication(_userId);
        _calendar.NextEventId = null;

        var created = await _service.CreateAsync(_userId, application.Id, new CreateInterviewRequest
        {
            ScheduledAtUtc = Slot,
        });

        Assert.NotNull(created);
        Assert.False(created.OnCalendar);

        var stored = await _fixture.Db.InterviewEvents.AsNoTracking().SingleAsync();
        Assert.Null(stored.CalendarEventId);
    }

    [Fact]
    public async Task CreateAsync_RejectsAnApplicationBelongingToSomeoneElse()
    {
        var application = _fixture.SeedApplication(_otherUserId);

        var created = await _service.CreateAsync(_userId, application.Id, new CreateInterviewRequest
        {
            ScheduledAtUtc = Slot,
        });

        Assert.Null(created);
        Assert.Empty(_fixture.Db.InterviewEvents);
    }

    [Fact]
    public async Task UpdateAsync_MovesTheInterviewAndUpdatesTheSameCalendarEvent()
    {
        var application = _fixture.SeedApplication(_userId);
        var interview = _fixture.SeedInterview(application.Id, Slot, calendarEventId: "cal-event-1");
        var moved = Slot.AddDays(1);

        var updated = await _service.UpdateAsync(_userId, interview.Id, new UpdateInterviewRequest
        {
            ScheduledAtUtc = moved,
            Kind = InterviewKind.Onsite,
            Notes = "Rescheduled",
        });

        Assert.NotNull(updated);
        Assert.Equal(moved, updated.ScheduledAtUtc);
        Assert.Equal(InterviewKind.Onsite, updated.Kind);

        // Same row synced again rather than a second event created.
        Assert.Equal([interview.Id], _calendar.UpsertedInterviewIds);
        var stored = await _fixture.Db.InterviewEvents.AsNoTracking().SingleAsync();
        Assert.Equal("cal-event-1", stored.CalendarEventId);
    }

    [Fact]
    public async Task UpdateAsync_RejectsAnInterviewBelongingToSomeoneElse()
    {
        var application = _fixture.SeedApplication(_otherUserId);
        var interview = _fixture.SeedInterview(application.Id, Slot);

        var updated = await _service.UpdateAsync(_userId, interview.Id, new UpdateInterviewRequest
        {
            ScheduledAtUtc = Slot.AddDays(1),
        });

        Assert.Null(updated);
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheCalendarCopyBeforeDroppingTheRow()
    {
        var application = _fixture.SeedApplication(_userId);
        var interview = _fixture.SeedInterview(application.Id, Slot, calendarEventId: "cal-event-9");

        var deleted = await _service.DeleteAsync(_userId, interview.Id);

        Assert.True(deleted);
        Assert.Equal(["cal-event-9"], _calendar.DeletedEventIds);
        Assert.Empty(_fixture.Db.InterviewEvents);
    }

    [Fact]
    public async Task DeleteAsync_SkipsTheCalendarCall_WhenTheInterviewWasNeverSynced()
    {
        var application = _fixture.SeedApplication(_userId);
        var interview = _fixture.SeedInterview(application.Id, Slot);

        var deleted = await _service.DeleteAsync(_userId, interview.Id);

        Assert.True(deleted);
        Assert.Empty(_calendar.DeletedEventIds);
    }

    [Fact]
    public async Task DeleteAsync_RejectsAnInterviewBelongingToSomeoneElse()
    {
        var application = _fixture.SeedApplication(_otherUserId);
        var interview = _fixture.SeedInterview(application.Id, Slot);

        Assert.False(await _service.DeleteAsync(_userId, interview.Id));
        Assert.Single(_fixture.Db.InterviewEvents);
    }

    [Fact]
    public async Task BuildCalendarFileAsync_RendersAnImportableEventWithAStableIdentity()
    {
        var application = _fixture.SeedApplication(_userId, company: "Acme; Inc");
        var interview = _fixture.SeedInterview(application.Id, Slot, InterviewKind.Onsite);

        var ics = await _service.BuildCalendarFileAsync(_userId, interview.Id);

        Assert.NotNull(ics);
        Assert.StartsWith("BEGIN:VCALENDAR", ics);
        Assert.EndsWith("END:VCALENDAR\r\n", ics);
        Assert.Contains($"UID:{interview.Id}@careerconnect", ics);
        Assert.Contains("DTSTART:20260910T140000Z", ics);
        Assert.Contains("DTEND:20260910T150000Z", ics);
        // The semicolon in the company name has to be escaped, or it reads as
        // a property separator and the event silently loses its title.
        Assert.Contains(@"SUMMARY:Onsite — Acme\; Inc", ics);
    }

    [Fact]
    public async Task BuildCalendarFileAsync_RefusesAnInterviewBelongingToSomeoneElse()
    {
        var application = _fixture.SeedApplication(_otherUserId);
        var interview = _fixture.SeedInterview(application.Id, Slot);

        Assert.Null(await _service.BuildCalendarFileAsync(_userId, interview.Id));
    }

    [Fact]
    public async Task ListAsync_ReturnsOnlyThisApplicationsInterviews_SoonestFirst()
    {
        var application = _fixture.SeedApplication(_userId);
        var other = _fixture.SeedApplication(_userId, company: "Other Co");
        _fixture.SeedInterview(application.Id, Slot.AddDays(3));
        _fixture.SeedInterview(application.Id, Slot);
        _fixture.SeedInterview(other.Id, Slot.AddDays(1));

        var interviews = await _service.ListAsync(_userId, application.Id);

        Assert.Equal(2, interviews.Count);
        Assert.Equal([Slot, Slot.AddDays(3)], interviews.Select(i => i.ScheduledAtUtc));
    }

    [Fact]
    public async Task RecordFromEmailAsync_SchedulesAnInterviewStampedAsComingFromEmail()
    {
        var application = _fixture.SeedApplication(_userId);

        var recorded = await _service.RecordFromEmailAsync(
            _userId, application.Id, Slot, InterviewKind.PhoneScreen, "From the invite");

        Assert.NotNull(recorded);
        Assert.Equal(ChangeSource.EmailSuggestion, recorded.Source);
        Assert.Equal(Slot, recorded.ScheduledAtUtc);
    }

    [Fact]
    public async Task RecordFromEmailAsync_IgnoresARepeatOfAnInterviewAlreadyScheduled()
    {
        var application = _fixture.SeedApplication(_userId);
        _fixture.SeedInterview(application.Id, Slot);

        // A reminder email about the same interview, an hour off in the parse.
        var recorded = await _service.RecordFromEmailAsync(
            _userId, application.Id, Slot.AddHours(1), InterviewKind.PhoneScreen, null);

        Assert.Null(recorded);
        Assert.Single(_fixture.Db.InterviewEvents);
    }

    [Fact]
    public async Task RecordFromEmailAsync_SchedulesASecondRound_WhenItIsWellClearOfTheFirst()
    {
        var application = _fixture.SeedApplication(_userId);
        _fixture.SeedInterview(application.Id, Slot);

        var recorded = await _service.RecordFromEmailAsync(
            _userId, application.Id, Slot.AddDays(7), InterviewKind.Onsite, null);

        Assert.NotNull(recorded);
        Assert.Equal(2, await _fixture.Db.InterviewEvents.CountAsync());
    }

    [Fact]
    public async Task RecordFromEmailAsync_RejectsAnApplicationBelongingToSomeoneElse()
    {
        var application = _fixture.SeedApplication(_otherUserId);

        var recorded = await _service.RecordFromEmailAsync(
            _userId, application.Id, Slot, InterviewKind.PhoneScreen, null);

        Assert.Null(recorded);
        Assert.Empty(_fixture.Db.InterviewEvents);
    }
}
