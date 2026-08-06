using CareerConnect.Api.Contracts;
using CareerConnect.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace CareerConnect.Api.Tests;

public sealed class ResumeServiceTests : IDisposable
{
    private readonly TestDatabase _fixture = new();
    private readonly FakeResumeFileTextExtractor _extractor = new();
    private readonly ResumeService _service;
    private readonly Guid _userId;

    public ResumeServiceTests()
    {
        _userId = _fixture.SeedUser("me@example.com");
        _service = new ResumeService(_fixture.Db, _extractor);
    }

    public void Dispose() => _fixture.Dispose();

    private static SaveResumeRequest Request(string label = "Primary") => new()
    {
        Label = label,
        Content = new string('x', 200),
    };

    [Fact]
    public async Task CreateAsync_MakesTheFirstResumeActive()
    {
        var first = await _service.CreateAsync(_userId, Request("First"));
        var second = await _service.CreateAsync(_userId, Request("Second"));

        Assert.True(first.IsActive);
        Assert.False(second.IsActive);
    }

    [Fact]
    public async Task SetActiveAsync_DeactivatesThePrevious()
    {
        var first = await _service.CreateAsync(_userId, Request("First"));
        var second = await _service.CreateAsync(_userId, Request("Second"));

        await _service.SetActiveAsync(_userId, second.Id);

        var active = await _service.GetActiveAsync(_userId);
        Assert.NotNull(active);
        Assert.Equal(second.Id, active.Id);
        var reloadedFirst = await _service.GetAsync(_userId, first.Id);
        Assert.False(reloadedFirst!.IsActive);
    }

    [Fact]
    public async Task ListAsync_ReturnsOnlyOwnResumes()
    {
        var otherUserId = _fixture.SeedUser("someone-else@example.com");
        await _service.CreateAsync(_userId, Request("Mine"));
        await _service.CreateAsync(otherUserId, Request("Theirs"));

        var list = await _service.ListAsync(_userId);

        var only = Assert.Single(list);
        Assert.Equal("Mine", only.Label);
    }

    [Fact]
    public async Task DeleteAsync_IsBlockedWhenMatchResultsReferenceTheResume()
    {
        var application = _fixture.SeedApplication(_userId);
        var resume = _fixture.SeedResume(_userId);
        var scoring = new MatchScoringService(_fixture.Db, new FakeResumeMatchAnalyzer());
        await scoring.ScoreAsync(_userId, application.Id);

        var outcome = await _service.DeleteAsync(_userId, resume.Id);

        Assert.Equal(DeleteResumeOutcome.HasMatchResults, outcome);
        Assert.NotNull(await _service.GetAsync(_userId, resume.Id));
    }

    [Fact]
    public async Task DeleteAsync_PromotesAnotherResumeWhenTheActiveOneIsRemoved()
    {
        var first = await _service.CreateAsync(_userId, Request("First"));
        var second = await _service.CreateAsync(_userId, Request("Second"));

        var outcome = await _service.DeleteAsync(_userId, first.Id);

        Assert.Equal(DeleteResumeOutcome.Deleted, outcome);
        var active = await _service.GetActiveAsync(_userId);
        Assert.NotNull(active);
        Assert.Equal(second.Id, active.Id);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsNotFoundForAnotherUsersResume()
    {
        var otherUserId = _fixture.SeedUser("someone-else@example.com");
        var theirs = await _service.CreateAsync(otherUserId, Request());

        var outcome = await _service.DeleteAsync(_userId, theirs.Id);

        Assert.Equal(DeleteResumeOutcome.NotFound, outcome);
        Assert.Equal(1, await _fixture.Db.Resumes.CountAsync());
    }

    [Fact]
    public async Task CreateFromFileAsync_CreatesResumeFromExtractedTextWithFilenameLabel()
    {
        using var stream = new MemoryStream();

        var outcome = await _service.CreateFromFileAsync(_userId, stream, "My Resume.pdf", label: null);

        var success = Assert.IsType<ResumeUploadOutcome.Success>(outcome);
        Assert.Equal("My Resume", success.Resume.Label);
        Assert.Equal(_extractor.Result, success.Resume.Content);
        Assert.True(success.Resume.IsActive);
    }

    [Fact]
    public async Task CreateFromFileAsync_PrefersExplicitLabelOverFilename()
    {
        using var stream = new MemoryStream();

        var outcome = await _service.CreateFromFileAsync(_userId, stream, "resume.docx", label: "Backend-focused");

        var success = Assert.IsType<ResumeUploadOutcome.Success>(outcome);
        Assert.Equal("Backend-focused", success.Resume.Label);
    }

    [Fact]
    public async Task CreateFromFileAsync_FailsForUnsupportedExtension()
    {
        _extractor.Result = null;
        using var stream = new MemoryStream();

        var outcome = await _service.CreateFromFileAsync(_userId, stream, "resume.txt", label: null);

        var failed = Assert.IsType<ResumeUploadOutcome.Failed>(outcome);
        Assert.Contains(".pdf", failed.Message);
        Assert.Empty(await _fixture.Db.Resumes.ToListAsync());
    }

    [Fact]
    public async Task CreateFromFileAsync_FailsWhenExtractedTextIsTooShort()
    {
        _extractor.Result = "too short";
        using var stream = new MemoryStream();

        var outcome = await _service.CreateFromFileAsync(_userId, stream, "resume.pdf", label: null);

        var failed = Assert.IsType<ResumeUploadOutcome.Failed>(outcome);
        Assert.Contains("scanned", failed.Message);
        Assert.Empty(await _fixture.Db.Resumes.ToListAsync());
    }

    [Fact]
    public async Task UpdateAsync_ChangesContentAndKeepsActiveFlag()
    {
        var created = await _service.CreateAsync(_userId, Request("Primary"));

        var updated = await _service.UpdateAsync(_userId, created.Id, new SaveResumeRequest
        {
            Label = "Primary (tailored)",
            Content = new string('y', 300),
        });

        Assert.NotNull(updated);
        Assert.Equal("Primary (tailored)", updated.Label);
        Assert.Equal(300, updated.Content.Length);
        Assert.True(updated.IsActive);
    }
}
