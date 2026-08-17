using CareerConnect.Api.Domain;
using CareerConnect.Api.Services;

namespace CareerConnect.Api.Tests;

public sealed class GmailUpdateScannerTests : IDisposable
{
    private readonly TestDatabase _fixture = new();
    private readonly FakeGmailOAuthService _oauth = new();
    private readonly FakeGmailMailReader _mailReader = new();
    private readonly FakeEmailStatusClassifier _classifier = new();
    private readonly GmailUpdateScanner _scanner;
    private readonly Guid _userId;

    public GmailUpdateScannerTests()
    {
        _userId = _fixture.SeedUser("me@example.com");
        _scanner = new GmailUpdateScanner(_fixture.Db, _oauth, _mailReader, _classifier);
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
