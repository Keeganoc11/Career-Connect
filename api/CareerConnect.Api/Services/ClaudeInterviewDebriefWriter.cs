using CareerConnect.Api.Domain;
using static CareerConnect.Api.Services.ClaudeStructuredCaller;

namespace CareerConnect.Api.Services;

/// <summary>One logged question and how the answer went, as the debrief sees it.</summary>
public record DebriefQuestion(string Text, string Kind, string? Answer, string Quality);

public record DebriefContext(
    string CompanyName,
    string RoleTitle,
    InterviewKind Kind,
    string JobDescriptionText,
    string? ResumeText,
    string? ResearchNotes,
    string? Reflection,
    int? SelfRating,
    IReadOnlyList<DebriefQuestion> Questions);

public interface IInterviewDebriefWriter
{
    bool IsConfigured { get; }

    Task<InterviewDebrief> WriteAsync(DebriefContext context, CancellationToken cancellationToken = default);
}

public class ClaudeInterviewDebriefWriter(ClaudeStructuredCaller caller) : IInterviewDebriefWriter
{
    private const string SystemPrompt = """
        Debrief one interview round for the candidate who sat it, scored against
        the job description.

        Be blunt. This exists so the next round goes better, which means saying
        when a round went badly and why. Encouragement that isn't earned makes
        the whole thing worthless.

        - score: 0-100, how well this round answered what the posting asks for.
          Judge only what the notes actually show. Sparse notes mean a low
          confidence, not a high score — say so in the verdict rather than
          inventing performance that isn't recorded.
        - verdict: a few blunt sentences on how it went, in the second person.
        - covered: requirements from the posting this round genuinely evidenced,
          each with the answer or note that shows it. Never claim coverage the
          notes don't support.
        - gaps: requirements the round exposed or left untouched — what happened,
          and the specific fix.
        - practice: the answers worth rehearsing before the next round, quoting
          the question.
        - next_round: what to prepare for whatever comes next.

        Work only from the notes, the questions and the posting. Do not invent
        questions that were asked, and do not assume anything about the outcome:
        nothing here says whether they got it.
        """;

    public bool IsConfigured => caller.IsConfigured;

    public async Task<InterviewDebrief> WriteAsync(
        DebriefContext context, CancellationToken cancellationToken = default)
    {
        var questions = context.Questions.Count == 0
            ? "(none logged)"
            : string.Join("\n\n", context.Questions.Select(q => $"""
                [{q.Kind} · answer rated {q.Quality}] {q.Text}
                Answer given: {(string.IsNullOrWhiteSpace(q.Answer) ? "(not written down)" : q.Answer)}
                """));

        var userPrompt = $"""
            Company: {context.CompanyName}
            Role: {context.RoleTitle}
            Round: {context.Kind}
            Their own rating of the round: {(context.SelfRating is { } r ? $"{r} out of 5" : "not given")}

            <job_description>
            {context.JobDescriptionText}
            </job_description>

            <resume>
            {context.ResumeText ?? "(not available)"}
            </resume>

            <research_notes>
            {context.ResearchNotes ?? "(none)"}
            </research_notes>

            <their_reflection>
            {context.Reflection ?? "(none written)"}
            </their_reflection>

            <questions_logged>
            {questions}
            </questions_logged>
            """;

        var schema = SchemaObject(
            new
            {
                score = new
                {
                    type = "integer",
                    minimum = 0,
                    maximum = 100,
                    description = "How well this round answered what the posting asks for.",
                },
                verdict = SchemaString("A few blunt sentences, second person, on how the round went."),
                covered = SchemaArray(
                    SchemaObject(
                        new
                        {
                            requirement = SchemaString("The requirement from the posting."),
                            evidence = SchemaString("The answer or note that shows it."),
                        },
                        "requirement", "evidence"),
                    "Requirements this round genuinely evidenced."),
                gaps = SchemaArray(
                    SchemaObject(
                        new
                        {
                            requirement = SchemaString("The requirement from the posting."),
                            what_happened = SchemaString("What the round showed, or that it never came up."),
                            fix = SchemaString("The specific thing to do about it."),
                        },
                        "requirement", "what_happened", "fix"),
                    "Requirements the round exposed or left untouched."),
                practice = SchemaArray(
                    SchemaString("An answer worth rehearsing, quoting the question."),
                    "Answers to rehearse before the next round."),
                next_round = SchemaArray(
                    SchemaString("One thing to prepare."),
                    "What to prepare for the next round."),
            },
            "score", "verdict", "covered", "gaps", "practice", "next_round");

        var payload = await caller.CallAsync<DebriefPayload>(
            "writing your interview debrief", SystemPrompt, userPrompt, schema, cancellationToken, maxTokens: 8000);

        return new InterviewDebrief
        {
            // The schema constrains this, but a score outside the band would
            // silently break every bar that renders it.
            Score = Math.Clamp(payload.Score, 0, 100),
            Verdict = payload.Verdict.Trim(),
            Covered = payload.Covered.Select(c => new CoveredRequirement(c.Requirement, c.Evidence)).ToList(),
            Gaps = payload.Gaps.Select(g => new ProbedGap(g.Requirement, g.WhatHappened, g.Fix)).ToList(),
            Practice = payload.Practice,
            NextRound = payload.NextRound,
            GeneratedAtUtc = DateTime.UtcNow,
        };
    }

    private sealed record DebriefPayload(
        int Score,
        string Verdict,
        List<CoveredPayload> Covered,
        List<GapPayload> Gaps,
        List<string> Practice,
        List<string> NextRound);

    private sealed record CoveredPayload(string Requirement, string Evidence);

    private sealed record GapPayload(string Requirement, string WhatHappened, string Fix);
}
