using System.ComponentModel.DataAnnotations;
using CareerConnect.Api.Domain;

namespace CareerConnect.Api.Contracts;

public class TrackedQuestionResponse
{
    public required Guid Id { get; init; }
    public required InterviewQuestionSide Side { get; init; }
    public required InterviewQuestionKind Kind { get; init; }
    public required string Text { get; init; }
    public string? Answer { get; init; }
    public AnswerQuality? Quality { get; init; }
    public required bool Asked { get; init; }
    public required bool Suggested { get; init; }
    public required int Position { get; init; }

    public static TrackedQuestionResponse From(InterviewQuestionEntry entry) => new()
    {
        Id = entry.Id,
        Side = entry.Side,
        Kind = entry.Kind,
        Text = entry.Text,
        Answer = entry.Answer,
        Quality = entry.Quality,
        Asked = entry.Asked,
        Suggested = entry.Suggested,
        Position = entry.Position,
    };
}

/// <summary>Everything one interview's page needs, in one request.</summary>
public class InterviewTrackerResponse
{
    public required Guid InterviewId { get; init; }
    public required Guid ApplicationId { get; init; }
    public required string CompanyName { get; init; }
    public required string RoleTitle { get; init; }
    public required DateTime ScheduledAtUtc { get; init; }
    public required InterviewKind Kind { get; init; }
    public string? Notes { get; init; }
    public string? ResearchNotes { get; init; }
    public string? Reflection { get; init; }
    public int? SelfRating { get; init; }

    /// <summary>Suggestions and the debrief both need one; without it, the page says so instead of failing.</summary>
    public required bool HasJobDescription { get; init; }

    public required List<TrackedQuestionResponse> Questions { get; init; }
    public InterviewDebrief? Debrief { get; init; }
}

public class SaveResearchRequest
{
    [MaxLength(20000)]
    public string? ResearchNotes { get; init; }
}

public class SaveReflectionRequest
{
    [MaxLength(20000)]
    public string? Reflection { get; init; }

    [Range(1, 5)]
    public int? SelfRating { get; init; }
}

public class CreateInterviewQuestionRequest
{
    public InterviewQuestionSide Side { get; init; } = InterviewQuestionSide.TheyAsked;
    public InterviewQuestionKind Kind { get; init; } = InterviewQuestionKind.Other;

    [Required, MaxLength(1000)]
    public required string Text { get; init; }
}

/// <summary>
/// A whole-question update: the page edits one question at a time and sends it
/// back complete, so a null Answer or Quality means "cleared", not "unchanged".
/// </summary>
public class UpdateInterviewQuestionRequest
{
    [Required, MaxLength(1000)]
    public required string Text { get; init; }

    public InterviewQuestionKind Kind { get; init; } = InterviewQuestionKind.Other;

    [MaxLength(8000)]
    public string? Answer { get; init; }

    public AnswerQuality? Quality { get; init; }

    public bool Asked { get; init; }
}

/// <summary>One question you've been asked, and everywhere it has come up.</summary>
public class QuestionBankEntryResponse
{
    public required string Text { get; init; }
    public required InterviewQuestionKind Kind { get; init; }
    public required int TimesAsked { get; init; }

    /// <summary>How many of those times you rated your answer weak — what makes this worth practising.</summary>
    public required int WeakAnswers { get; init; }

    public required List<string> Companies { get; init; }
    public required DateTime LastAskedAtUtc { get; init; }

    /// <summary>The most recent answer you wrote down, to start from when rehearsing.</summary>
    public string? LastAnswer { get; init; }

    /// <summary>The interview that answer came from, so the page can link back to it.</summary>
    public required Guid LastInterviewId { get; init; }
}

public class QuestionBankResponse
{
    public required List<QuestionBankEntryResponse> Questions { get; init; }

    /// <summary>The subset worth rehearsing — weak answers first. Already inside Questions.</summary>
    public required List<QuestionBankEntryResponse> Practice { get; init; }
}
