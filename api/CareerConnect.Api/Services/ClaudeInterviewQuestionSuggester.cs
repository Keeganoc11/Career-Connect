using CareerConnect.Api.Domain;
using static CareerConnect.Api.Services.ClaudeStructuredCaller;

namespace CareerConnect.Api.Services;

public record SuggestedQuestion(InterviewQuestionSide Side, InterviewQuestionKind Kind, string Text);

public interface IInterviewQuestionSuggester
{
    bool IsConfigured { get; }

    /// <summary>
    /// Questions worth having written down before a round: the ones they're
    /// likely to ask, and sharp ones to ask them.
    /// </summary>
    Task<List<SuggestedQuestion>> SuggestAsync(
        string companyName,
        string roleTitle,
        InterviewKind kind,
        string jobDescriptionText,
        string resumeText,
        IReadOnlyCollection<string> alreadyListed,
        CancellationToken cancellationToken = default);
}

public class ClaudeInterviewQuestionSuggester(ClaudeStructuredCaller caller) : IInterviewQuestionSuggester
{
    private const string SystemPrompt = """
        Prepare a candidate for one specific interview round.

        Produce two kinds of question:

        1. side "they_ask" — questions this interviewer is likely to ask in THIS
           round. Match the round: a phone screen is motivation, background and
           logistics; a technical round is the stack and the problems the posting
           names; an onsite or final round goes deeper on design, judgement and
           working with people. Include the questions an interviewer who read
           this resume against this posting would ask about the gaps between
           them — those are the ones that decide the round.

        2. side "you_ask" — questions for the candidate to ask. They must be
           answerable only by someone who works there, specific to this company,
           team, product or posting. No questions whose answer is on the careers
           page, and nothing that reads as a test of the interviewer.

        Between 6 and 10 questions in total, weighted towards "they_ask". One
        sentence each, no preamble, no numbering, no bracketed placeholders.
        Never repeat a question that is already listed.
        """;

    public bool IsConfigured => caller.IsConfigured;

    public async Task<List<SuggestedQuestion>> SuggestAsync(
        string companyName,
        string roleTitle,
        InterviewKind kind,
        string jobDescriptionText,
        string resumeText,
        IReadOnlyCollection<string> alreadyListed,
        CancellationToken cancellationToken = default)
    {
        var userPrompt = $"""
            Company: {companyName}
            Role: {roleTitle}
            Round: {kind}

            <job_description>
            {jobDescriptionText}
            </job_description>

            <resume>
            {resumeText}
            </resume>

            <already_listed>
            {(alreadyListed.Count == 0 ? "(nothing yet)" : string.Join("\n", alreadyListed))}
            </already_listed>
            """;

        var schema = SchemaObject(
            new
            {
                questions = SchemaArray(
                    SchemaObject(
                        new
                        {
                            side = SchemaEnum("Who asks it.", "they_ask", "you_ask"),
                            kind = SchemaEnum(
                                "What sort of question it is.",
                                "behavioral", "technical", "system_design", "role", "company", "logistics", "other"),
                            text = SchemaString("The question itself, one sentence."),
                        },
                        "side", "kind", "text"),
                    "Between 6 and 10 questions for this round."),
            },
            "questions");

        var payload = await caller.CallAsync<SuggestionsPayload>(
            "suggesting interview questions", SystemPrompt, userPrompt, schema, cancellationToken, maxTokens: 4000);

        return payload.Questions
            .Where(q => !string.IsNullOrWhiteSpace(q.Text))
            .Select(q => new SuggestedQuestion(ParseSide(q.Side), ParseKind(q.Kind), q.Text.Trim()))
            .ToList();
    }

    // The model answers in snake_case names chosen for the schema, which don't
    // match the enum spellings — and an unknown value is a suggestion worth
    // keeping, so both fall back rather than throwing.
    private static InterviewQuestionSide ParseSide(string value) =>
        value == "you_ask" ? InterviewQuestionSide.YouAsk : InterviewQuestionSide.TheyAsked;

    private static InterviewQuestionKind ParseKind(string value) => value switch
    {
        "behavioral" => InterviewQuestionKind.Behavioral,
        "technical" => InterviewQuestionKind.Technical,
        "system_design" => InterviewQuestionKind.SystemDesign,
        "role" => InterviewQuestionKind.Role,
        "company" => InterviewQuestionKind.Company,
        "logistics" => InterviewQuestionKind.Logistics,
        _ => InterviewQuestionKind.Other,
    };

    private sealed record SuggestionsPayload(List<SuggestionPayload> Questions);

    private sealed record SuggestionPayload(string Side, string Kind, string Text);
}
