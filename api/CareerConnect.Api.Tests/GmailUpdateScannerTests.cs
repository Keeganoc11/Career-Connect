using CareerConnect.Api.Domain;
using CareerConnect.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace CareerConnect.Api.Tests;

public sealed class GmailUpdateScannerTests : IDisposable
{
    private readonly TestDatabase _fixture = new();
    private readonly FakeGmailOAuthService _oauth = new();
    private readonly FakeGmailMailReader _mailReader = new();
    private readonly FakeEmailStatusClassifier _classifier = new();
    private readonly FakeInterviewDetailsExtractor _interviewExtractor = new();
    private readonly FakeInterviewCalendarSync _calendar = new();
    private readonly GmailUpdateScanner _scanner;
    private readonly Guid _userId;

    public GmailUpdateScannerTests()
    {
        _userId = _fixture.SeedUser("me@example.com");
        var automation = new ApplicationAutomation(
            _fixture.Db,
            new InterviewService(_fixture.Db, _calendar),
            new ConfigurationBuilder().Build(),
            NullLogger<ApplicationAutomation>.Instance);
        _scanner = new GmailUpdateScanner(_fixture.Db, _oauth, _mailReader, _classifier, _interviewExtractor, automation);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task ScanAsync_FailsWhenGmailNotConnected()
    {
        _oauth.Connection = null;

        var outcome = await _scanner.ScanAsync(_userId);

        var failed = Assert.IsType<GmailScanOutcome.Failed>(outcome);
        Assert.Contains("Connect Gmail", failed.Message);
    }

    [Fact]
    public async Task ScanAsync_FailsWhenNoApiKeyConfigured()
    {
        _classifier.IsConfigured = false;

        var outcome = await _scanner.ScanAsync(_userId);

        var failed = Assert.IsType<GmailScanOutcome.Failed>(outcome);
        Assert.Contains("Anthropic API key", failed.Message);
    }

    [Fact]
    public async Task ScanAsync_SucceedsWithNoTrackedApplicationsSoNewApplicationsCanStillBeFound()
    {
        // No applications seeded at all. This used to fail outright; now an
        // empty tracker is a valid state — the scan should still run and
        // just look for brand-new application confirmations.
        _mailReader.Result = [new CandidateEmail(0, "Thanks for applying", "careers@acme.com", "snippet", DateTime.UtcNow)];
        _classifier.NewApplicationResult =
        [
            new EmailNewApplicationMatch(0, "Acme", "Software Engineer", "Application confirmation."),
        ];

        var outcome = await _scanner.ScanAsync(_userId);

        var success = Assert.IsType<GmailScanOutcome.Success>(outcome);
        Assert.Empty(success.StatusUpdates);
        var suggestion = Assert.Single(success.NewApplications);
        Assert.Equal("Acme", suggestion.CompanyName);
        Assert.Equal("Software Engineer", suggestion.RoleTitle);
    }

    [Fact]
    public async Task ScanAsync_FiltersOutNewApplicationSuggestionsForAlreadyTrackedCompanies()
    {
        _fixture.SeedApplication(_userId, "Acme", status: ApplicationStatus.Applied);
        _mailReader.Result = [new CandidateEmail(0, "Thanks for applying", "careers@acme.com", "snippet", DateTime.UtcNow)];
        _classifier.NewApplicationResult =
        [
            new EmailNewApplicationMatch(0, "Acme", "Software Engineer", "Should have been a status match, not new."),
        ];

        var outcome = await _scanner.ScanAsync(_userId);

        var success = Assert.IsType<GmailScanOutcome.Success>(outcome);
        Assert.Empty(success.NewApplications);
    }

    [Fact]
    public async Task ScanAsync_DedupesRepeatedNewApplicationCompaniesWithinAScan()
    {
        _mailReader.Result =
        [
            new CandidateEmail(0, "Thanks for applying", "careers@acme.com", "snippet", DateTime.UtcNow),
            new CandidateEmail(1, "We received your application", "hr@acme.com", "snippet", DateTime.UtcNow),
        ];
        _classifier.NewApplicationResult =
        [
            new EmailNewApplicationMatch(0, "Acme", "Software Engineer", "First confirmation."),
            new EmailNewApplicationMatch(1, "acme", "", "Duplicate confirmation for the same company."),
        ];

        var outcome = await _scanner.ScanAsync(_userId);

        var success = Assert.IsType<GmailScanOutcome.Success>(outcome);
        Assert.Single(success.NewApplications);
    }

    [Fact]
    public async Task ScanAsync_ExcludesRejectedAndWithdrawnFromClassifierContext()
    {
        _fixture.SeedApplication(_userId, "Rejected Co", status: ApplicationStatus.Rejected);
        _fixture.SeedApplication(_userId, "Withdrawn Co", status: ApplicationStatus.Withdrawn);
        _fixture.SeedApplication(_userId, "Open Co", status: ApplicationStatus.Applied);
        _mailReader.Result = [new CandidateEmail(0, "Subject", "from@x.com", "snippet", DateTime.UtcNow)];

        await _scanner.ScanAsync(_userId);

        var sentCompanies = _classifier.LastApplications!.Select(a => a.CompanyName).ToList();
        Assert.Single(sentCompanies);
        Assert.Contains("Open Co", sentCompanies);
    }

    [Fact]
    public async Task ScanAsync_MapsAConfidentMatchToASuggestion()
    {
        var app = _fixture.SeedApplication(_userId, "Acme", status: ApplicationStatus.Applied);
        _mailReader.Result =
        [
            new CandidateEmail(0, "Interview invitation", "recruiter@acme.com", "We'd like to schedule...", DateTime.UtcNow),
        ];
        _classifier.Result =
        [
            new EmailClassificationMatch(EmailIndex: 0, ApplicationIndex: 0, SuggestedStatus: "Interview", Reasoning: "Explicit interview invite."),
        ];

        var outcome = await _scanner.ScanAsync(_userId);

        var success = Assert.IsType<GmailScanOutcome.Success>(outcome);
        var suggestion = Assert.Single(success.StatusUpdates);
        Assert.Equal(app.Id, suggestion.ApplicationId);
        Assert.Equal(ApplicationStatus.Applied, suggestion.CurrentStatus);
        Assert.Equal(ApplicationStatus.Interview, suggestion.SuggestedStatus);
        Assert.Equal("Interview invitation", suggestion.EmailSubject);
        Assert.Equal("recruiter@acme.com", suggestion.EmailFrom);
    }

    /// <summary>Seeds one interview-invite email whose body the extractor will be asked to read.</summary>
    private void SeedInterviewEmail(string messageId = "msg-1")
    {
        _mailReader.Result =
        [
            new CandidateEmail(0, "Interview invitation", "recruiter@acme.com", "We'd like to schedule...",
                DateTime.UtcNow, messageId),
        ];
        _mailReader.Bodies = new Dictionary<string, string>
        {
            [messageId] = "We'd like to meet Thursday September 10th at 2pm Eastern.",
        };
        _classifier.Result =
        [
            new EmailClassificationMatch(0, 0, "Interview", "Explicit interview invite."),
        ];
    }

    [Fact]
    public async Task ScanAsync_ReadsTheInterviewTimeOutOfTheEmailBody()
    {
        _fixture.SeedApplication(_userId, "Acme", status: ApplicationStatus.Applied);
        SeedInterviewEmail();
        var scheduled = new DateTimeOffset(2026, 9, 10, 14, 0, 0, TimeSpan.FromHours(-4));
        _interviewExtractor.ResultByIndex = new() { [0] = (scheduled, "Technical") };

        var outcome = await _scanner.ScanAsync(_userId);

        var suggestion = Assert.Single(Assert.IsType<GmailScanOutcome.Success>(outcome).StatusUpdates);
        Assert.Equal(scheduled.UtcDateTime, suggestion.InterviewAtUtc);
        Assert.Equal(InterviewKind.Technical, suggestion.InterviewKind);
    }

    [Fact]
    public async Task ScanAsync_LeavesTheTimeUnset_WhenTheEmailNeverNamedOne()
    {
        _fixture.SeedApplication(_userId, "Acme", status: ApplicationStatus.Applied);
        SeedInterviewEmail();
        _interviewExtractor.ResultByIndex = new() { [0] = (null, "PhoneScreen") };

        var outcome = await _scanner.ScanAsync(_userId);

        var suggestion = Assert.Single(Assert.IsType<GmailScanOutcome.Success>(outcome).StatusUpdates);
        Assert.Null(suggestion.InterviewAtUtc);
    }

    [Fact]
    public async Task ScanAsync_OnlyOpensBodiesOfEmailsThatLookLikeInterviews()
    {
        _fixture.SeedApplication(_userId, "Acme", status: ApplicationStatus.Applied);
        _mailReader.Result =
        [
            new CandidateEmail(0, "Thanks but no", "recruiter@acme.com", "unfortunately", DateTime.UtcNow, "msg-1"),
        ];
        _classifier.Result = [new EmailClassificationMatch(0, 0, "Rejected", "Explicit rejection.")];

        await _scanner.ScanAsync(_userId);

        // A rejection's body has nothing to schedule — never fetched, never sent to a model.
        Assert.Empty(_mailReader.LastRequestedBodyIds);
        Assert.Equal(0, _interviewExtractor.CallCount);
    }

    [Fact]
    public async Task ScanAsync_StillReturnsTheSuggestion_WhenReadingTheBodyFails()
    {
        _fixture.SeedApplication(_userId, "Acme", status: ApplicationStatus.Applied);
        SeedInterviewEmail();
        _mailReader.ThrowOnReadBodies = new InvalidOperationException("Gmail hiccup");

        var outcome = await _scanner.ScanAsync(_userId);

        var suggestion = Assert.Single(Assert.IsType<GmailScanOutcome.Success>(outcome).StatusUpdates);
        Assert.Equal(ApplicationStatus.Interview, suggestion.SuggestedStatus);
        Assert.Null(suggestion.InterviewAtUtc);
    }

    [Fact]
    public async Task ScanAsync_SkipsTheExtractionPassEntirely_WhenNoApiKeyIsConfigured()
    {
        _fixture.SeedApplication(_userId, "Acme", status: ApplicationStatus.Applied);
        SeedInterviewEmail();
        _interviewExtractor.IsConfigured = false;

        var outcome = await _scanner.ScanAsync(_userId);

        Assert.Single(Assert.IsType<GmailScanOutcome.Success>(outcome).StatusUpdates);
        Assert.Empty(_mailReader.LastRequestedBodyIds);
        Assert.Equal(0, _interviewExtractor.CallCount);
    }

    [Fact]
    public async Task ScanAsync_GivesTheExtractorTheCompanyAndRoleForContext()
    {
        _fixture.SeedApplication(_userId, "Acme", status: ApplicationStatus.Applied);
        SeedInterviewEmail();
        _interviewExtractor.ResultByIndex = new() { [0] = (DateTimeOffset.UtcNow.AddDays(3), "Onsite") };

        await _scanner.ScanAsync(_userId);

        var context = Assert.Single(_interviewExtractor.LastEmails);
        Assert.Equal("Acme", context.CompanyName);
        Assert.Equal("Software Engineer", context.RoleTitle);
        Assert.Contains("Thursday September 10th", context.Body);
    }

    [Fact]
    public async Task ScanAsync_AutoAppliesAPreparingApplicationOnItsConfirmationEmail()
    {
        var app = _fixture.SeedApplication(_userId, "Acme", status: ApplicationStatus.Preparing);
        var confirmedAt = new DateTime(2026, 8, 20, 14, 0, 0, DateTimeKind.Utc);
        _mailReader.Result =
        [
            new CandidateEmail(0, "We received your application", "careers@acme.com", "Thanks!", confirmedAt),
        ];
        _classifier.Result = [new EmailClassificationMatch(0, 0, "Applied", "Confirmation of submission.")];

        var outcome = await _scanner.ScanAsync(_userId);

        var success = Assert.IsType<GmailScanOutcome.Success>(outcome);
        Assert.Empty(success.StatusUpdates);
        var applied = Assert.Single(success.AutoApplied);
        Assert.Equal(app.Id, applied.ApplicationId);

        var stored = await _fixture.Db.Applications.AsNoTracking().FirstAsync(a => a.Id == app.Id);
        Assert.Equal(ApplicationStatus.Applied, stored.Status);
        Assert.Equal(new DateOnly(2026, 8, 20), stored.DateApplied);

        var change = await _fixture.Db.StatusChanges.AsNoTracking()
            .SingleAsync(c => c.ApplicationId == app.Id && c.ToStatus == ApplicationStatus.Applied);
        Assert.Equal(ChangeSource.EmailAutomatic, change.Source);
    }

    [Fact]
    public async Task ScanAsync_AppliesAClearRejectionWithoutAsking()
    {
        var app = _fixture.SeedApplication(_userId, "Acme", status: ApplicationStatus.Applied);
        _mailReader.Result = [new CandidateEmail(0, "Your application", "talent@acme.com", "We won't be moving forward", DateTime.UtcNow)];
        _classifier.Result = [new EmailClassificationMatch(0, 0, "Rejected", "Says they won't move forward.", "clear")];

        var outcome = await _scanner.ScanAsync(_userId);

        var success = Assert.IsType<GmailScanOutcome.Success>(outcome);
        Assert.Empty(success.StatusUpdates);
        var applied = Assert.Single(success.AutoApplied);
        Assert.Equal(ApplicationStatus.Rejected, applied.ToStatus);
        Assert.NotNull(applied.ActivityId);
        Assert.Equal(ApplicationStatus.Rejected,
            (await _fixture.Db.Applications.AsNoTracking().FirstAsync(a => a.Id == app.Id)).Status);
    }

    [Fact]
    public async Task ScanAsync_StillSuggests_WhenTheRejectionIsOnlyLikely()
    {
        _fixture.SeedApplication(_userId, "Acme", status: ApplicationStatus.Applied);
        _mailReader.Result = [new CandidateEmail(0, "Update", "talent@acme.com", "We've had a lot of applicants", DateTime.UtcNow)];
        _classifier.Result = [new EmailClassificationMatch(0, 0, "Rejected", "Sounds like a no.", "likely")];

        var success = Assert.IsType<GmailScanOutcome.Success>(await _scanner.ScanAsync(_userId));

        Assert.Empty(success.AutoApplied);
        Assert.Single(success.StatusUpdates);
    }

    [Fact]
    public async Task ScanAsync_AppliesAClearInvite_AndSchedulesTheInterview()
    {
        var app = _fixture.SeedApplication(_userId, "Acme", status: ApplicationStatus.Applied);
        SeedInterviewEmail();
        _classifier.Result = [new EmailClassificationMatch(0, 0, "Interview", "Explicit interview invite.", "clear")];
        var scheduled = new DateTimeOffset(2026, 9, 10, 14, 0, 0, TimeSpan.FromHours(-4));
        _interviewExtractor.ResultByIndex = new() { [0] = (scheduled, "Technical") };

        var success = Assert.IsType<GmailScanOutcome.Success>(await _scanner.ScanAsync(_userId));

        Assert.Single(success.AutoApplied);
        var interview = await _fixture.Db.InterviewEvents.AsNoTracking().SingleAsync(i => i.ApplicationId == app.Id);
        Assert.Equal(scheduled.UtcDateTime, interview.ScheduledAtUtc);
        Assert.Equal(InterviewKind.Technical, interview.Kind);
    }

    [Fact]
    public async Task ScanAsync_NeverAppliesAnOffer_HoweverClear()
    {
        _fixture.SeedApplication(_userId, "Acme", status: ApplicationStatus.Interview);
        _mailReader.Result = [new CandidateEmail(0, "Offer letter", "talent@acme.com", "We're pleased to offer", DateTime.UtcNow)];
        _classifier.Result = [new EmailClassificationMatch(0, 0, "Offer", "An offer.", "clear")];

        var success = Assert.IsType<GmailScanOutcome.Success>(await _scanner.ScanAsync(_userId));

        Assert.Empty(success.AutoApplied);
        Assert.Single(success.StatusUpdates);
    }

    [Fact]
    public async Task ScanAsync_StillOnlySuggests_WhenAPreparingApplicationJumpsStraightToInterview()
    {
        // Skipping Applied entirely is unusual enough to be worth a human look,
        // so only the Preparing → Applied confirmation is ever automatic.
        var app = _fixture.SeedApplication(_userId, "Acme", status: ApplicationStatus.Preparing);
        _mailReader.Result = [new CandidateEmail(0, "Let's talk", "recruiter@acme.com", "snippet", DateTime.UtcNow)];
        _classifier.Result = [new EmailClassificationMatch(0, 0, "Interview", "Interview invite.")];

        var outcome = await _scanner.ScanAsync(_userId);

        var success = Assert.IsType<GmailScanOutcome.Success>(outcome);
        Assert.Empty(success.AutoApplied);
        Assert.Single(success.StatusUpdates);

        var stored = await _fixture.Db.Applications.AsNoTracking().FirstAsync(a => a.Id == app.Id);
        Assert.Equal(ApplicationStatus.Preparing, stored.Status);
    }

    [Fact]
    public async Task ScanAsync_FiltersOutMatchesThatDontChangeStatus()
    {
        _fixture.SeedApplication(_userId, "Acme", status: ApplicationStatus.Interview);
        _mailReader.Result = [new CandidateEmail(0, "Interview reminder", "a@b.com", "snippet", DateTime.UtcNow)];
        _classifier.Result =
        [
            new EmailClassificationMatch(0, 0, "Interview", "Same status as already recorded."),
        ];

        var outcome = await _scanner.ScanAsync(_userId);

        var success = Assert.IsType<GmailScanOutcome.Success>(outcome);
        Assert.Empty(success.StatusUpdates);
    }

    [Fact]
    public async Task ScanAsync_SkipsMatchesWithOutOfRangeIndexes()
    {
        _fixture.SeedApplication(_userId, "Acme", status: ApplicationStatus.Applied);
        _mailReader.Result = [new CandidateEmail(0, "Subject", "a@b.com", "snippet", DateTime.UtcNow)];
        _classifier.Result = [new EmailClassificationMatch(EmailIndex: 5, ApplicationIndex: 0, SuggestedStatus: "Interview", Reasoning: "bad index")];

        var outcome = await _scanner.ScanAsync(_userId);

        var success = Assert.IsType<GmailScanOutcome.Success>(outcome);
        Assert.Empty(success.StatusUpdates);
    }

    [Fact]
    public async Task ScanAsync_SkipsMatchesWithUnparsableStatus()
    {
        _fixture.SeedApplication(_userId, "Acme", status: ApplicationStatus.Applied);
        _mailReader.Result = [new CandidateEmail(0, "Subject", "a@b.com", "snippet", DateTime.UtcNow)];
        _classifier.Result = [new EmailClassificationMatch(0, 0, "NotARealStatus", "bad status")];

        var outcome = await _scanner.ScanAsync(_userId);

        var success = Assert.IsType<GmailScanOutcome.Success>(outcome);
        Assert.Empty(success.StatusUpdates);
    }

    [Fact]
    public async Task ScanAsync_AdvancesWatermarkEvenWithNoMatches()
    {
        _fixture.SeedApplication(_userId, "Acme");
        _mailReader.Result = [];

        await _scanner.ScanAsync(_userId);

        Assert.Equal(1, _oauth.MarkCheckedCallCount);
    }

    [Fact]
    public async Task ScanAsync_PassesTheStoredWatermarkToTheMailReader()
    {
        var watermark = DateTime.UtcNow.AddDays(-3);
        _oauth.Connection = _oauth.Connection! with { LastCheckedAtUtc = watermark };
        _fixture.SeedApplication(_userId, "Acme");

        await _scanner.ScanAsync(_userId);

        Assert.Equal(watermark, _mailReader.LastAfterArgument);
    }

    [Fact]
    public async Task ScanAsync_SurfacesMailReaderFailureAsFailedOutcome()
    {
        _fixture.SeedApplication(_userId, "Acme");
        _mailReader.ThrowOnRead = new InvalidOperationException("Gmail is not connected.");

        var outcome = await _scanner.ScanAsync(_userId);

        var failed = Assert.IsType<GmailScanOutcome.Failed>(outcome);
        Assert.Contains("Couldn't read Gmail", failed.Message);
        Assert.Equal(0, _oauth.MarkCheckedCallCount);
    }

    [Fact]
    public async Task ScanAsync_SurfacesClassifierFailureAsFailedOutcome()
    {
        _fixture.SeedApplication(_userId, "Acme");
        _mailReader.Result = [new CandidateEmail(0, "Subject", "a@b.com", "snippet", DateTime.UtcNow)];
        _classifier.ThrowOnClassify = new InvalidOperationException("boom");

        var outcome = await _scanner.ScanAsync(_userId);

        var failed = Assert.IsType<GmailScanOutcome.Failed>(outcome);
        Assert.Contains("classification failed", failed.Message);
    }
}
