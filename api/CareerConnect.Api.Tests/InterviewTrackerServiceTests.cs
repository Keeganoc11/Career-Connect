using CareerConnect.Api.Contracts;
using CareerConnect.Api.Domain;
using CareerConnect.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CareerConnect.Api.Tests;

public sealed class InterviewTrackerServiceTests : IDisposable
{
    private readonly TestDatabase _fixture = new();
    private readonly FakeInterviewQuestionSuggester _suggester = new();
    private readonly FakeInterviewDebriefWriter _debriefWriter = new();
    private readonly InterviewTrackerService _service;
    private readonly Guid _userId;

    public InterviewTrackerServiceTests()
    {
        _userId = _fixture.SeedUser("me@example.com");
        _service = new InterviewTrackerService(
            _fixture.Db, _suggester, _debriefWriter, NullLogger<InterviewTrackerService>.Instance);
    }

    public void Dispose() => _fixture.Dispose();

    private InterviewEvent SeedInterview(string company = "Acme", string? jobDescription = "Backend role, .NET and Postgres.")
    {
        var application = _fixture.SeedApplication(_userId, company, jobDescription);
        return _fixture.SeedInterview(application.Id);
    }

    private Task<TrackedQuestionResponse?> AddAsync(
        Guid interviewId,
        string text,
        InterviewQuestionSide side = InterviewQuestionSide.TheyAsked,
        InterviewQuestionKind kind = InterviewQuestionKind.Behavioral) =>
        _service.AddQuestionAsync(_userId, interviewId, new CreateInterviewQuestionRequest
        {
            Side = side,
            Kind = kind,
            Text = text,
        });

    [Fact]
    public async Task GetAsync_ReturnsTheRoundAndItsJob()
    {
        var interview = SeedInterview("Northwind");

        var prep = await _service.GetAsync(_userId, interview.Id);

        Assert.NotNull(prep);
        Assert.Equal("Northwind", prep.CompanyName);
        Assert.True(prep.HasJobDescription);
        Assert.Empty(prep.Questions);
        Assert.Null(prep.Debrief);
    }

    [Fact]
    public async Task GetAsync_IsScopedToTheOwner()
    {
        var interview = SeedInterview();
        var someoneElse = _fixture.SeedUser("other@example.com");

        Assert.Null(await _service.GetAsync(someoneElse, interview.Id));
    }

    [Fact]
    public async Task ResearchAndReflectionAreSavedSeparately()
    {
        var interview = SeedInterview();

        await _service.SaveResearchAsync(_userId, interview.Id, new SaveResearchRequest
        {
            ResearchNotes = "  They shipped a payments rewrite last quarter.  ",
        });
        var saved = await _service.SaveReflectionAsync(_userId, interview.Id, new SaveReflectionRequest
        {
            Reflection = "Froze on the caching question.",
            SelfRating = 3,
        });

        Assert.NotNull(saved);
        Assert.Equal("They shipped a payments rewrite last quarter.", saved.ResearchNotes);
        Assert.Equal("Froze on the caching question.", saved.Reflection);
        Assert.Equal(3, saved.SelfRating);
    }

    [Fact]
    public async Task QuestionsAreOrderedPerSide()
    {
        var interview = SeedInterview();

        await AddAsync(interview.Id, "First for them");
        await AddAsync(interview.Id, "Second for them");
        await AddAsync(interview.Id, "First for me", InterviewQuestionSide.YouAsk);

        var prep = await _service.GetAsync(_userId, interview.Id);

        var theyAsked = prep!.Questions.Where(q => q.Side == InterviewQuestionSide.TheyAsked).ToList();
        var youAsk = prep.Questions.Where(q => q.Side == InterviewQuestionSide.YouAsk).ToList();
        Assert.Equal([0, 1], theyAsked.Select(q => q.Position));
        // Positions run per side, so the first question to ask them is 0 too.
        Assert.Equal([0], youAsk.Select(q => q.Position));
    }

    [Fact]
    public async Task UpdateQuestion_RecordsTheAnswerAndClaimsTheSuggestion()
    {
        var interview = SeedInterview();
        await _service.SuggestQuestionsAsync(_userId, interview.Id);
        var suggested = (await _service.GetAsync(_userId, interview.Id))!.Questions
            .First(q => q.Side == InterviewQuestionSide.TheyAsked);
        Assert.True(suggested.Suggested);

        var updated = await _service.UpdateQuestionAsync(_userId, suggested.Id, new UpdateInterviewQuestionRequest
        {
            Text = suggested.Text,
            Kind = InterviewQuestionKind.Technical,
            Answer = "Talked through the deadlock we hit in the prep runner.",
            Quality = AnswerQuality.Okay,
        });

        Assert.NotNull(updated);
        Assert.Equal(AnswerQuality.Okay, updated.Quality);
        Assert.False(updated.Suggested);
    }

    [Fact]
    public async Task DeleteQuestion_IsScopedToTheOwner()
    {
        var interview = SeedInterview();
        var question = await AddAsync(interview.Id, "Tell me about yourself.");
        var someoneElse = _fixture.SeedUser("other@example.com");

        Assert.False(await _service.DeleteQuestionAsync(someoneElse, question!.Id));
        Assert.True(await _service.DeleteQuestionAsync(_userId, question.Id));
        Assert.Empty(await _fixture.Db.InterviewQuestions.ToListAsync());
    }

    [Fact]
    public async Task SuggestQuestions_AddsBothSidesAndSkipsWhatIsAlreadyListed()
    {
        var interview = SeedInterview();
        await AddAsync(interview.Id, "Walk me through a race condition you fixed!");

        var outcome = await _service.SuggestQuestionsAsync(_userId, interview.Id);

        var added = Assert.IsType<InterviewPrepOutcome<List<TrackedQuestionResponse>>.Success>(outcome).Value;
        // The suggester offered that same question back with different
        // punctuation; only the genuinely new one is kept.
        Assert.Single(added);
        Assert.Equal(InterviewQuestionSide.YouAsk, added[0].Side);
        Assert.Contains("Walk me through a race condition you fixed!", _suggester.LastAlreadyListed);
    }

    [Fact]
    public async Task SuggestQuestions_NeedsAJobDescription()
    {
        var interview = SeedInterview(jobDescription: null);

        var outcome = await _service.SuggestQuestionsAsync(_userId, interview.Id);

        Assert.IsType<InterviewPrepOutcome<List<TrackedQuestionResponse>>.NotReady>(outcome);
        Assert.Equal(0, _suggester.CallCount);
    }

    [Fact]
    public async Task SuggestQuestions_SaysSoWhenThereIsNoApiKey()
    {
        _suggester.IsConfigured = false;
        var interview = SeedInterview();

        Assert.IsType<InterviewPrepOutcome<List<TrackedQuestionResponse>>.Unavailable>(
            await _service.SuggestQuestionsAsync(_userId, interview.Id));
    }

    [Fact]
    public async Task SuggestQuestions_KeepsWhatWasAlreadyThere_WhenTheCallFails()
    {
        var interview = SeedInterview();
        await AddAsync(interview.Id, "Tell me about yourself.");
        _suggester.ThrowOnSuggest = new InvalidOperationException("Claude is down.");

        var outcome = await _service.SuggestQuestionsAsync(_userId, interview.Id);

        Assert.IsType<InterviewPrepOutcome<List<TrackedQuestionResponse>>.Failed>(outcome);
        Assert.Single(await _fixture.Db.InterviewQuestions.ToListAsync());
    }

    [Fact]
    public async Task Debrief_ScoresTheRoundAndStoresIt()
    {
        var interview = SeedInterview();
        var question = await AddAsync(interview.Id, "How do you handle a flaky test?");
        await _service.UpdateQuestionAsync(_userId, question!.Id, new UpdateInterviewQuestionRequest
        {
            Text = question.Text,
            Kind = InterviewQuestionKind.Technical,
            Answer = "Quarantine it, then fix the timing assumption.",
            Quality = AnswerQuality.Strong,
        });

        var outcome = await _service.GenerateDebriefAsync(_userId, interview.Id);

        var debrief = Assert.IsType<InterviewPrepOutcome<InterviewDebrief>.Success>(outcome).Value;
        Assert.Equal(62, debrief.Score);

        // Stored, so reopening the page doesn't spend another model call.
        var reloaded = await _service.GetAsync(_userId, interview.Id);
        Assert.Equal(62, reloaded!.Debrief!.Score);

        // Only real questions are sent — a suggestion nobody confirmed would
        // read as something that actually happened in the room.
        Assert.Single(_debriefWriter.LastContext!.Questions);
        Assert.Equal("Strong", _debriefWriter.LastContext.Questions[0].Quality);
    }

    [Fact]
    public async Task Debrief_NeedsSomethingToScore()
    {
        var interview = SeedInterview();
        // Suggestions alone aren't evidence of anything happening.
        await _service.SuggestQuestionsAsync(_userId, interview.Id);

        var outcome = await _service.GenerateDebriefAsync(_userId, interview.Id);

        Assert.IsType<InterviewPrepOutcome<InterviewDebrief>.NotReady>(outcome);
    }

    [Fact]
    public async Task Debrief_RunsOnAReflectionAlone()
    {
        var interview = SeedInterview();
        await _service.SaveReflectionAsync(_userId, interview.Id, new SaveReflectionRequest
        {
            Reflection = "Two hours of system design and I never asked about the traffic shape.",
            SelfRating = 2,
        });

        Assert.IsType<InterviewPrepOutcome<InterviewDebrief>.Success>(
            await _service.GenerateDebriefAsync(_userId, interview.Id));
    }

    [Fact]
    public async Task Debrief_KeepsTheOldOne_WhenTheCallFails()
    {
        var interview = SeedInterview();
        await AddAsync(interview.Id, "Why this company?");
        await _service.GenerateDebriefAsync(_userId, interview.Id);
        _debriefWriter.ThrowOnWrite = new InvalidOperationException("Claude is down.");

        var outcome = await _service.GenerateDebriefAsync(_userId, interview.Id);

        Assert.IsType<InterviewPrepOutcome<InterviewDebrief>.Failed>(outcome);
        Assert.Equal(62, (await _service.GetAsync(_userId, interview.Id))!.Debrief!.Score);
    }

    [Fact]
    public async Task QuestionBank_GroupsTheSameQuestionAcrossCompanies()
    {
        var first = SeedInterview("Acme");
        var second = SeedInterview("Globex");

        var a = await AddAsync(first.Id, "Tell me about a time you disagreed with your lead.");
        var b = await AddAsync(second.Id, "Tell me about a time you disagreed with your lead?");
        await _service.UpdateQuestionAsync(_userId, a!.Id, new UpdateInterviewQuestionRequest
        {
            Text = a.Text,
            Kind = InterviewQuestionKind.Behavioral,
            Answer = "Rambled.",
            Quality = AnswerQuality.Weak,
        });
        await _service.UpdateQuestionAsync(_userId, b!.Id, new UpdateInterviewQuestionRequest
        {
            Text = b.Text,
            Kind = InterviewQuestionKind.Behavioral,
            Answer = "Used the caching disagreement.",
            Quality = AnswerQuality.Okay,
        });

        var bank = await _service.GetQuestionBankAsync(_userId);

        var entry = Assert.Single(bank.Questions);
        Assert.Equal(2, entry.TimesAsked);
        Assert.Equal(1, entry.WeakAnswers);
        Assert.Equal(["Acme", "Globex"], entry.Companies);
        Assert.Single(bank.Practice);
    }

    [Fact]
    public async Task QuestionBank_LeavesOutSuggestionsAndQuestionsYouAsked()
    {
        var interview = SeedInterview();
        await _service.SuggestQuestionsAsync(_userId, interview.Id);
        await AddAsync(interview.Id, "What does success look like in 90 days?", InterviewQuestionSide.YouAsk);

        var bank = await _service.GetQuestionBankAsync(_userId);

        Assert.Empty(bank.Questions);
    }

    [Fact]
    public async Task QuestionBank_IsScopedToTheOwner()
    {
        var interview = SeedInterview();
        await AddAsync(interview.Id, "Tell me about yourself.");
        var someoneElse = _fixture.SeedUser("other@example.com");

        Assert.Empty((await _service.GetQuestionBankAsync(someoneElse)).Questions);
    }
}
