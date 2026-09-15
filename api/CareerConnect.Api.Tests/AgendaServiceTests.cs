using CareerConnect.Api.Contracts;
using CareerConnect.Api.Domain;
using CareerConnect.Api.Services;

namespace CareerConnect.Api.Tests;

public sealed class AgendaServiceTests : IDisposable
{
    private readonly TestDatabase _fixture = new();
    private readonly AgendaService _service;
    private readonly Guid _userId;
    private readonly Guid _otherUserId;

    public AgendaServiceTests()
    {
        _userId = _fixture.SeedUser("me@example.com");
        _otherUserId = _fixture.SeedUser("someone-else@example.com");
        _service = new AgendaService(_fixture.Db);
    }

    public void Dispose() => _fixture.Dispose();

    /// <summary>
    /// Staleness is measured from UpdatedAtUtc, which SaveChanges stamps to now
    /// — so aging a row means writing the column directly afterwards.
    /// </summary>
    private Application SeedAged(ApplicationStatus status, int idleDays, string company = "Acme")
    {
        var application = _fixture.SeedApplication(_userId, company: company, status: status);
        application.UpdatedAtUtc = DateTime.UtcNow.AddDays(-idleDays);
        _fixture.Db.SaveChanges();
        return application;
    }

    private void SeedPrepRun(Guid applicationId, bool readyToApply)
    {
        _fixture.Db.PrepRuns.Add(new PrepRun
        {
            Id = Guid.NewGuid(),
            ApplicationId = applicationId,
            Status = PrepRunStatus.Succeeded,
            ReadyToApply = readyToApply,
            StartedAtUtc = DateTime.UtcNow.AddDays(-1),
            Steps = [],
        });
        _fixture.Db.SaveChanges();
    }

    [Fact]
    public async Task GetAsync_NudgesAnApplicationThatClearedPrepAndWasNeverSent()
    {
        var application = SeedAged(ApplicationStatus.Preparing, idleDays: 3);
        SeedPrepRun(application.Id, readyToApply: true);

        var agenda = await _service.GetAsync(_userId);

        var nudge = Assert.Single(agenda.Nudges);
        Assert.Equal(NudgeKind.ReadyToApply, nudge.Kind);
    }

    [Fact]
    public async Task GetAsync_LeavesAFreshlyPreppedApplicationAlone()
    {
        var application = SeedAged(ApplicationStatus.Preparing, idleDays: 1);
        SeedPrepRun(application.Id, readyToApply: true);

        var agenda = await _service.GetAsync(_userId);

        Assert.Empty(agenda.Nudges);
    }

    [Fact]
    public async Task GetAsync_NudgesAPostingSavedLongAgoAndNeverPrepped()
    {
        SeedAged(ApplicationStatus.Preparing, idleDays: 10);

        var agenda = await _service.GetAsync(_userId);

        Assert.Equal(NudgeKind.NeverPrepped, Assert.Single(agenda.Nudges).Kind);
    }

    [Theory]
    [InlineData(20, NudgeKind.Silent)]
    [InlineData(45, NudgeKind.ProbablyGhosted)]
    public async Task GetAsync_EscalatesASilentApplicationAsItAges(int idleDays, NudgeKind expected)
    {
        SeedAged(ApplicationStatus.Applied, idleDays);

        var agenda = await _service.GetAsync(_userId);

        var nudge = Assert.Single(agenda.Nudges);
        Assert.Equal(expected, nudge.Kind);
        Assert.Equal(idleDays, nudge.DaysSinceActivity);
    }

    [Fact]
    public async Task GetAsync_LeavesARecentlyAppliedApplicationAlone()
    {
        SeedAged(ApplicationStatus.Applied, idleDays: 5);

        Assert.Empty((await _service.GetAsync(_userId)).Nudges);
    }

    [Fact]
    public async Task GetAsync_ChasesAnUnansweredOfferSooner()
    {
        SeedAged(ApplicationStatus.Offer, idleDays: 4);

        Assert.Equal(NudgeKind.AwaitingYou, Assert.Single((await _service.GetAsync(_userId)).Nudges).Kind);
    }

    [Fact]
    public async Task GetAsync_IgnoresApplicationsThatAreAlreadyOver()
    {
        SeedAged(ApplicationStatus.Rejected, idleDays: 90);
        SeedAged(ApplicationStatus.Withdrawn, idleDays: 90, company: "Withdrawn Co");
        SeedAged(ApplicationStatus.Ghosted, idleDays: 90, company: "Ghosted Co");

        var agenda = await _service.GetAsync(_userId);

        Assert.Empty(agenda.Nudges);
        Assert.Empty(agenda.UpcomingInterviews);
    }

    [Fact]
    public async Task GetAsync_StaysQuietAboutAStaleApplicationThatHasAnInterviewBooked()
    {
        var application = SeedAged(ApplicationStatus.Interview, idleDays: 30);
        _fixture.SeedInterview(application.Id, DateTime.UtcNow.AddDays(5));

        var agenda = await _service.GetAsync(_userId);

        Assert.Empty(agenda.Nudges);
        Assert.Single(agenda.UpcomingInterviews);
    }

    [Fact]
    public async Task GetAsync_SurfacesAnImminentInterviewOnlyAsACard_NotAlsoAsANudge()
    {
        // A nudge here would always duplicate the card: the urgency window is
        // narrower than the upcoming window, so it can never fire alone.
        var application = SeedAged(ApplicationStatus.Interview, idleDays: 1);
        _fixture.SeedInterview(application.Id, DateTime.UtcNow.AddHours(20));

        var agenda = await _service.GetAsync(_userId);

        Assert.Empty(agenda.Nudges);
        var upcoming = Assert.Single(agenda.UpcomingInterviews);
        Assert.False(upcoming.HasPrep);
    }

    [Fact]
    public async Task GetAsync_ReportsPrepAsReady_WhenItHasBeenGeneratedForTheApplication()
    {
        var application = SeedAged(ApplicationStatus.Interview, idleDays: 1);
        application.InterviewPrepJson = """{"questions":[],"talkingPoints":[]}""";
        _fixture.Db.SaveChanges();
        _fixture.SeedInterview(application.Id, DateTime.UtcNow.AddHours(20));

        var agenda = await _service.GetAsync(_userId);

        Assert.Empty(agenda.Nudges);
        Assert.True(Assert.Single(agenda.UpcomingInterviews).HasPrep);
    }

    [Fact]
    public async Task GetAsync_ListsUpcomingInterviewsSoonestFirst()
    {
        var first = _fixture.SeedApplication(_userId, company: "First Co");
        var second = _fixture.SeedApplication(_userId, company: "Second Co");
        _fixture.SeedInterview(second.Id, DateTime.UtcNow.AddDays(6));
        _fixture.SeedInterview(first.Id, DateTime.UtcNow.AddDays(2));

        var upcoming = (await _service.GetAsync(_userId)).UpcomingInterviews;

        Assert.Equal(["First Co", "Second Co"], upcoming.Select(i => i.CompanyName));
    }

    [Fact]
    public async Task GetAsync_LeavesOutInterviewsAlreadyPastOrTooFarOut()
    {
        var application = _fixture.SeedApplication(_userId);
        _fixture.SeedInterview(application.Id, DateTime.UtcNow.AddDays(-1));
        _fixture.SeedInterview(application.Id, DateTime.UtcNow.AddDays(40));

        Assert.Empty((await _service.GetAsync(_userId)).UpcomingInterviews);
    }

    [Fact]
    public async Task GetAsync_ShowsNothingFromAnotherUsersPipeline()
    {
        var theirs = SeedAged(ApplicationStatus.Applied, idleDays: 60);
        theirs.UserId = _otherUserId;
        _fixture.Db.SaveChanges();
        _fixture.SeedInterview(theirs.Id, DateTime.UtcNow.AddDays(3));

        var agenda = await _service.GetAsync(_userId);

        Assert.Empty(agenda.Nudges);
        Assert.Empty(agenda.UpcomingInterviews);
    }

    [Fact]
    public async Task GetAsync_PutsTheLongestNeglectedApplicationFirst()
    {
        SeedAged(ApplicationStatus.Applied, idleDays: 16, company: "Recent Co");
        SeedAged(ApplicationStatus.Applied, idleDays: 50, company: "Ancient Co");

        var nudges = (await _service.GetAsync(_userId)).Nudges;

        Assert.Equal(["Ancient Co", "Recent Co"], nudges.Select(n => n.CompanyName));
    }
}
