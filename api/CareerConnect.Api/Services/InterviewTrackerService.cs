using CareerConnect.Api.Contracts;
using CareerConnect.Api.Data;
using CareerConnect.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CareerConnect.Api.Services;

public abstract record InterviewPrepOutcome<T>
{
    public sealed record Success(T Value) : InterviewPrepOutcome<T>;
    public sealed record NotFound : InterviewPrepOutcome<T>;

    /// <summary>The request was fine; the data isn't ready — no job description, nothing logged yet.</summary>
    public sealed record NotReady(string Message) : InterviewPrepOutcome<T>;

    /// <summary>No API key configured.</summary>
    public sealed record Unavailable(string Message) : InterviewPrepOutcome<T>;

    /// <summary>The model call failed.</summary>
    public sealed record Failed(string Message) : InterviewPrepOutcome<T>;
}

public interface IInterviewTrackerService
{
    Task<InterviewTrackerResponse?> GetAsync(Guid userId, Guid interviewId, CancellationToken cancellationToken = default);

    Task<InterviewTrackerResponse?> SaveResearchAsync(
        Guid userId, Guid interviewId, SaveResearchRequest request, CancellationToken cancellationToken = default);

    Task<InterviewTrackerResponse?> SaveReflectionAsync(
        Guid userId, Guid interviewId, SaveReflectionRequest request, CancellationToken cancellationToken = default);

    Task<TrackedQuestionResponse?> AddQuestionAsync(
        Guid userId, Guid interviewId, CreateInterviewQuestionRequest request, CancellationToken cancellationToken = default);

    Task<TrackedQuestionResponse?> UpdateQuestionAsync(
        Guid userId, Guid questionId, UpdateInterviewQuestionRequest request, CancellationToken cancellationToken = default);

    Task<bool> DeleteQuestionAsync(Guid userId, Guid questionId, CancellationToken cancellationToken = default);

    /// <summary>Adds model-suggested questions for this round, skipping any already listed.</summary>
    Task<InterviewPrepOutcome<List<TrackedQuestionResponse>>> SuggestQuestionsAsync(
        Guid userId, Guid interviewId, CancellationToken cancellationToken = default);

    Task<InterviewPrepOutcome<InterviewDebrief>> GenerateDebriefAsync(
        Guid userId, Guid interviewId, CancellationToken cancellationToken = default);

    /// <summary>Every question you've been asked, across every interview, grouped by wording.</summary>
    Task<QuestionBankResponse> GetQuestionBankAsync(Guid userId, CancellationToken cancellationToken = default);
}

public class InterviewTrackerService(
    AppDbContext db,
    IInterviewQuestionSuggester suggester,
    IInterviewDebriefWriter debriefWriter,
    ILogger<InterviewTrackerService> logger) : IInterviewTrackerService
{
    public async Task<InterviewTrackerResponse?> GetAsync(
        Guid userId, Guid interviewId, CancellationToken cancellationToken = default)
    {
        var interview = await LoadAsync(userId, interviewId, tracked: false, cancellationToken);
        return interview is null ? null : ToResponse(interview);
    }

    public async Task<InterviewTrackerResponse?> SaveResearchAsync(
        Guid userId, Guid interviewId, SaveResearchRequest request, CancellationToken cancellationToken = default)
    {
        var interview = await LoadAsync(userId, interviewId, tracked: true, cancellationToken);
        if (interview is null)
        {
            return null;
        }

        interview.ResearchNotes = Normalize(request.ResearchNotes);
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(interview);
    }

    public async Task<InterviewTrackerResponse?> SaveReflectionAsync(
        Guid userId, Guid interviewId, SaveReflectionRequest request, CancellationToken cancellationToken = default)
    {
        var interview = await LoadAsync(userId, interviewId, tracked: true, cancellationToken);
        if (interview is null)
        {
            return null;
        }

        interview.Reflection = Normalize(request.Reflection);
        interview.SelfRating = request.SelfRating;
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(interview);
    }

    public async Task<TrackedQuestionResponse?> AddQuestionAsync(
        Guid userId, Guid interviewId, CreateInterviewQuestionRequest request, CancellationToken cancellationToken = default)
    {
        var interview = await LoadAsync(userId, interviewId, tracked: true, cancellationToken);
        if (interview is null)
        {
            return null;
        }

        var entry = NewQuestion(interview, request.Side, request.Kind, request.Text.Trim(), suggested: false);
        db.InterviewQuestions.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
        return TrackedQuestionResponse.From(entry);
    }

    public async Task<TrackedQuestionResponse?> UpdateQuestionAsync(
        Guid userId, Guid questionId, UpdateInterviewQuestionRequest request, CancellationToken cancellationToken = default)
    {
        var question = await db.InterviewQuestions
            .FirstOrDefaultAsync(
                q => q.Id == questionId && q.Interview.Application.UserId == userId, cancellationToken);

        if (question is null)
        {
            return null;
        }

        question.Text = request.Text.Trim();
        question.Kind = request.Kind;
        question.Answer = Normalize(request.Answer);
        question.Quality = request.Quality;
        question.Asked = request.Asked;

        // Editing a suggestion makes it yours — and only a question that really
        // came up belongs in the question bank.
        question.Suggested = false;

        await db.SaveChangesAsync(cancellationToken);
        return TrackedQuestionResponse.From(question);
    }

    public async Task<bool> DeleteQuestionAsync(
        Guid userId, Guid questionId, CancellationToken cancellationToken = default)
    {
        var question = await db.InterviewQuestions
            .FirstOrDefaultAsync(
                q => q.Id == questionId && q.Interview.Application.UserId == userId, cancellationToken);

        if (question is null)
        {
            return false;
        }

        db.InterviewQuestions.Remove(question);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<InterviewPrepOutcome<List<TrackedQuestionResponse>>> SuggestQuestionsAsync(
        Guid userId, Guid interviewId, CancellationToken cancellationToken = default)
    {
        var interview = await LoadAsync(userId, interviewId, tracked: true, cancellationToken);
        if (interview is null)
        {
            return new InterviewPrepOutcome<List<TrackedQuestionResponse>>.NotFound();
        }

        if (!suggester.IsConfigured)
        {
            return new InterviewPrepOutcome<List<TrackedQuestionResponse>>.Unavailable(
                "Suggested questions need an Anthropic API key.");
        }

        var jobDescription = interview.Application.JobDescriptionText;
        if (string.IsNullOrWhiteSpace(jobDescription))
        {
            return new InterviewPrepOutcome<List<TrackedQuestionResponse>>.NotReady(
                "Add the job description to this application first — the suggestions come from it.");
        }

        var resumeText = await ActiveResumeTextAsync(userId, cancellationToken);
        var existing = interview.Questions.Select(q => q.Text).ToList();

        List<SuggestedQuestion> suggestions;
        try
        {
            suggestions = await suggester.SuggestAsync(
                interview.Application.CompanyName,
                interview.Application.RoleTitle,
                interview.Kind,
                jobDescription,
                resumeText ?? "(no resume on file)",
                existing,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Suggesting questions failed for interview {InterviewId}.", interviewId);
            return new InterviewPrepOutcome<List<TrackedQuestionResponse>>.Failed(
                "Couldn’t get suggestions right now. Try again in a moment.");
        }

        // The prompt says not to repeat what's listed; this is what makes that
        // true, since a near-identical question would otherwise pile up every
        // time the button is pressed.
        var seen = existing.Select(Canonical).ToHashSet();
        var added = new List<InterviewQuestionEntry>();

        foreach (var suggestion in suggestions)
        {
            if (!seen.Add(Canonical(suggestion.Text)))
            {
                continue;
            }

            var entry = NewQuestion(interview, suggestion.Side, suggestion.Kind, suggestion.Text, suggested: true);
            db.InterviewQuestions.Add(entry);
            interview.Questions.Add(entry);
            added.Add(entry);
        }

        await db.SaveChangesAsync(cancellationToken);
        return new InterviewPrepOutcome<List<TrackedQuestionResponse>>.Success(
            added.Select(TrackedQuestionResponse.From).ToList());
    }

    public async Task<InterviewPrepOutcome<InterviewDebrief>> GenerateDebriefAsync(
        Guid userId, Guid interviewId, CancellationToken cancellationToken = default)
    {
        var interview = await LoadAsync(userId, interviewId, tracked: true, cancellationToken);
        if (interview is null)
        {
            return new InterviewPrepOutcome<InterviewDebrief>.NotFound();
        }

        if (!debriefWriter.IsConfigured)
        {
            return new InterviewPrepOutcome<InterviewDebrief>.Unavailable(
                "The debrief needs an Anthropic API key.");
        }

        var jobDescription = interview.Application.JobDescriptionText;
        if (string.IsNullOrWhiteSpace(jobDescription))
        {
            return new InterviewPrepOutcome<InterviewDebrief>.NotReady(
                "Add the job description to this application first — the debrief is scored against it.");
        }

        // Questions they asked are the substance; a debrief written from an
        // empty page would be the model guessing at how it went.
        var answered = interview.Questions
            .Where(q => q.Side == InterviewQuestionSide.TheyAsked && !q.Suggested)
            .ToList();

        if (answered.Count == 0 && string.IsNullOrWhiteSpace(interview.Reflection))
        {
            return new InterviewPrepOutcome<InterviewDebrief>.NotReady(
                "Log what they asked, or write a reflection, and the debrief has something to score.");
        }

        var context = new DebriefContext(
            interview.Application.CompanyName,
            interview.Application.RoleTitle,
            interview.Kind,
            jobDescription,
            await ActiveResumeTextAsync(userId, cancellationToken),
            interview.ResearchNotes,
            interview.Reflection,
            interview.SelfRating,
            answered
                .Select(q => new DebriefQuestion(
                    q.Text,
                    q.Kind.ToString(),
                    q.Answer,
                    q.Quality?.ToString() ?? "not rated"))
                .ToList());

        InterviewDebrief debrief;
        try
        {
            debrief = await debriefWriter.WriteAsync(context, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Debrief failed for interview {InterviewId}.", interviewId);
            return new InterviewPrepOutcome<InterviewDebrief>.Failed(
                "Couldn’t write the debrief right now. Try again in a moment.");
        }

        interview.Debrief = debrief;
        await db.SaveChangesAsync(cancellationToken);
        return new InterviewPrepOutcome<InterviewDebrief>.Success(debrief);
    }

    public async Task<QuestionBankResponse> GetQuestionBankAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        // Suggestions are excluded: the bank is a record of what has actually
        // been asked, and counting guesses would make it lie about frequency.
        var asked = await db.InterviewQuestions
            .AsNoTracking()
            .Where(q => q.Interview.Application.UserId == userId
                     && q.Side == InterviewQuestionSide.TheyAsked
                     && !q.Suggested)
            .Select(q => new
            {
                q.Text,
                q.Kind,
                q.Answer,
                q.Quality,
                q.InterviewEventId,
                q.Interview.ScheduledAtUtc,
                q.Interview.Application.CompanyName,
            })
            .ToListAsync(cancellationToken);

        var grouped = asked
            .GroupBy(q => Canonical(q.Text))
            .Select(group =>
            {
                var mostRecent = group.OrderByDescending(q => q.ScheduledAtUtc).First();
                return new QuestionBankEntryResponse
                {
                    // The wording of the most recent asking, not the canonical
                    // form — that one is for matching, and reads like a slug.
                    Text = mostRecent.Text,
                    Kind = mostRecent.Kind,
                    TimesAsked = group.Count(),
                    WeakAnswers = group.Count(q => q.Quality == AnswerQuality.Weak),
                    Companies = group.Select(q => q.CompanyName).Distinct().Order().ToList(),
                    LastAskedAtUtc = mostRecent.ScheduledAtUtc,
                    LastAnswer = group
                        .OrderByDescending(q => q.ScheduledAtUtc)
                        .Select(q => q.Answer)
                        .FirstOrDefault(answer => !string.IsNullOrWhiteSpace(answer)),
                    LastInterviewId = mostRecent.InterviewEventId,
                };
            })
            .OrderByDescending(entry => entry.TimesAsked)
            .ThenByDescending(entry => entry.LastAskedAtUtc)
            .ToList();

        return new QuestionBankResponse
        {
            Questions = grouped,
            // Worth rehearsing: it went badly, or it keeps coming back and has
            // no written answer to fall back on.
            Practice = grouped
                .Where(entry => entry.WeakAnswers > 0
                             || (entry.TimesAsked > 1 && string.IsNullOrWhiteSpace(entry.LastAnswer)))
                .OrderByDescending(entry => entry.WeakAnswers)
                .ThenByDescending(entry => entry.TimesAsked)
                .ToList(),
        };
    }

    private async Task<InterviewEvent?> LoadAsync(
        Guid userId, Guid interviewId, bool tracked, CancellationToken cancellationToken)
    {
        var query = db.InterviewEvents
            .Include(i => i.Application)
            .Include(i => i.Questions)
            .Where(i => i.Id == interviewId && i.Application.UserId == userId);

        if (!tracked)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<string?> ActiveResumeTextAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Resumes
            .AsNoTracking()
            .Where(r => r.UserId == userId && r.IsActive)
            .Select(r => r.Content)
            .FirstOrDefaultAsync(cancellationToken);

    private static InterviewQuestionEntry NewQuestion(
        InterviewEvent interview,
        InterviewQuestionSide side,
        InterviewQuestionKind kind,
        string text,
        bool suggested)
    {
        var lastPosition = interview.Questions
            .Where(q => q.Side == side)
            .Select(q => (int?)q.Position)
            .DefaultIfEmpty(null)
            .Max() ?? -1;

        return new InterviewQuestionEntry
        {
            Id = Guid.NewGuid(),
            InterviewEventId = interview.Id,
            Side = side,
            Kind = kind,
            Text = text,
            Suggested = suggested,
            Position = lastPosition + 1,
            CreatedAtUtc = DateTime.UtcNow,
        };
    }

    /// <summary>
    /// What counts as "the same question". Interviewers never phrase one
    /// identically twice, and exact matching would make the bank a list of
    /// singletons — so case, punctuation and spacing are ignored.
    /// </summary>
    private static string Canonical(string text)
    {
        var letters = text.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray();
        var words = new string(letters).ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static InterviewTrackerResponse ToResponse(InterviewEvent interview) => new()
    {
        InterviewId = interview.Id,
        ApplicationId = interview.ApplicationId,
        CompanyName = interview.Application.CompanyName,
        RoleTitle = interview.Application.RoleTitle,
        ScheduledAtUtc = interview.ScheduledAtUtc,
        Kind = interview.Kind,
        Notes = interview.Notes,
        ResearchNotes = interview.ResearchNotes,
        Reflection = interview.Reflection,
        SelfRating = interview.SelfRating,
        HasJobDescription = !string.IsNullOrWhiteSpace(interview.Application.JobDescriptionText),
        Questions = interview.Questions
            .OrderBy(q => q.Side)
            .ThenBy(q => q.Position)
            .Select(TrackedQuestionResponse.From)
            .ToList(),
        Debrief = interview.Debrief,
    };
}
