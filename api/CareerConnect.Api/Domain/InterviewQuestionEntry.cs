namespace CareerConnect.Api.Domain;

/// <summary>Which way the question goes — one round has both kinds.</summary>
public enum InterviewQuestionSide
{
    /// <summary>They asked it. The answer stored is yours, and it gets a quality.</summary>
    TheyAsked,

    /// <summary>You want to ask it. The answer stored is theirs, and it gets ticked off.</summary>
    YouAsk,
}

public enum InterviewQuestionKind
{
    Behavioral,
    Technical,
    SystemDesign,
    Role,
    Company,
    Logistics,
    Other,
}

/// <summary>How an answer actually went, judged by the person who gave it.</summary>
public enum AnswerQuality
{
    Strong,
    Okay,
    Weak,
}

/// <summary>
/// One question in one round, either direction.
///
/// A row rather than a blob on the interview, because the whole point of
/// logging questions is to see them across rounds and companies — "what do
/// they keep asking me, and where do I keep fumbling" is a query, not a note.
/// </summary>
public class InterviewQuestionEntry
{
    public Guid Id { get; set; }
    public Guid InterviewEventId { get; set; }

    public InterviewQuestionSide Side { get; set; }
    public InterviewQuestionKind Kind { get; set; }

    public required string Text { get; set; }

    /// <summary>Your answer when they asked it; theirs when you did.</summary>
    public string? Answer { get; set; }

    /// <summary>Only meaningful on a question they asked; null until it's been rated.</summary>
    public AnswerQuality? Quality { get; set; }

    /// <summary>Ticked off during the round — "I actually asked this".</summary>
    public bool Asked { get; set; }

    /// <summary>
    /// True when the model proposed it rather than the user typing it. Kept so
    /// a suggestion that was never touched can be told apart from a question
    /// that really came up — the question bank shouldn't count guesses.
    /// </summary>
    public bool Suggested { get; set; }

    /// <summary>Hand-ordered within its side, so a prepared list stays in the order you want to ask it.</summary>
    public int Position { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public InterviewEvent Interview { get; set; } = null!;
}
