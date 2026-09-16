using CareerConnect.Api.Domain;
using CareerConnect.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace CareerConnect.Api.Tests;

public sealed class FollowUpServiceTests : IDisposable
{
    private readonly TestDatabase _fixture = new();
    private readonly FakeFollowUpWriter _writer = new();
    private readonly FollowUpService _service;
    private readonly Guid _userId;

    public FollowUpServiceTests()
    {
        _userId = _fixture.SeedUser("me@example.com");
        _service = new FollowUpService(_fixture.Db, _writer, new ApplicationService(_fixture.Db, new FakeInterviewCalendarSync()));
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task DraftAsync_SignsWithTheNameOnTheBaseResume_AndMentionsThePastInterview()
    {
        var application = _fixture.SeedApplication(_userId, "Acme");
        _fixture.SeedResume(_userId, layout: TestResumes.Layout());
        _fixture.SeedInterview(application.Id, scheduledAtUtc: DateTime.UtcNow.AddDays(-9));

        var outcome = await _service.DraftAsync(_userId, application.Id);

        Assert.IsType<FollowUpOutcome.Drafted>(outcome);
        Assert.Equal("Jordan Rivera", _writer.LastContext!.CandidateName);
        Assert.Equal("Acme", _writer.LastContext.CompanyName);
        Assert.NotNull(_writer.LastContext.LastInterview);
    }

    [Fact]
    public async Task DraftAsync_IsNotFoundForSomeoneElsesApplication()
    {
        var otherUser = _fixture.SeedUser("someone@example.com");
        var theirs = _fixture.SeedApplication(otherUser);

        Assert.IsType<FollowUpOutcome.NotFound>(await _service.DraftAsync(_userId, theirs.Id));
        Assert.Null(_writer.LastContext);
    }

    [Fact]
    public async Task MarkSentAsync_RecordsTheFollowUp_AndRestartsTheSilence()
    {
        var application = _fixture.SeedApplication(_userId);
        application.UpdatedAtUtc = DateTime.UtcNow.AddDays(-20);
        _fixture.Db.SaveChanges();

        var updated = await _service.MarkSentAsync(_userId, application.Id);

        Assert.NotNull(updated!.LastFollowUpAtUtc);
        var stored = await _fixture.Db.Applications.AsNoTracking().FirstAsync(a => a.Id == application.Id);
        Assert.True(stored.UpdatedAtUtc > DateTime.UtcNow.AddMinutes(-1));
    }
}

public sealed class FakeFollowUpWriter : IFollowUpWriter
{
    public bool IsConfigured { get; set; } = true;
    public FollowUpContext? LastContext { get; private set; }

    public Task<FollowUpDraft> WriteAsync(FollowUpContext context, CancellationToken cancellationToken = default)
    {
        LastContext = context;
        return Task.FromResult(new FollowUpDraft("Following up", "Hi, …"));
    }
}
