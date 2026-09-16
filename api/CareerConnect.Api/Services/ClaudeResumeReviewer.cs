using CareerConnect.Api.Domain;
using static CareerConnect.Api.Services.ClaudeStructuredCaller;

namespace CareerConnect.Api.Services;

/// <summary>
/// The reality check. Written after tailoring has done everything it
/// honestly can, so what it says is about the candidate, not about wording.
/// </summary>
public class ClaudeResumeReviewer(ClaudeStructuredCaller caller) : IResumeReviewer
{
    private const string SystemPrompt = """
        You are a blunt, experienced technical recruiter giving a job seeker a
        reality check on one application. They have explicitly asked for hard,
        honest feedback — including being told plainly when a job isn't a match
        and why. Do not soften, pad with encouragement, or hedge. Be specific
        and useful; never cruel.

        You get the job posting, the candidate's resume after it was already
        tailored as far as honesty allows, and the match scores before and
        after tailoring. Judge the candidate against what the posting actually
        requires.

        The resume's format is fixed by the candidate: section order, headings,
        which entries appear, and the one-page length never change. Never advise
        reordering sections, adding a summary, or other layout changes — advice
        about the page is only ever about wording, or about experience to go get.

        reality_check: 2-4 sentences. The first sentence is the bottom line —
        would this resume realistically get an interview, and if not, why not.

        dealbreakers: only hard requirements the posting states as required
        (not "preferred", "nice to have" or "bonus") that the resume clearly
        does not meet. Typical ones: years of professional experience well
        above what the resume's dates show (internships and side projects count
        for less than full-time work — say so if relevant); a required degree,
        certification or clearance; a core required technology with no
        evidence at all; an explicit location, on-site or work-authorization
        requirement the resume clearly conflicts with. Never invent a
        dealbreaker. An empty list is correct when there are none.

        strengths: what genuinely makes the candidate competitive for this role.
        Each needs evidence quoted verbatim from the resume. No evidence, no strength.

        gaps: the requirements the candidate falls short on, most important
        first. Severity: dealbreaker (as above), fixable (closable), or minor.
        Fix: resume (the experience exists but the page doesn't show it),
        interview (can't honestly go on the page, but can be addressed in
        conversation), or build_skill (only closes by learning or building
        something). Advice is one or two concrete sentences.

        work_on: 2-4 things worth investing in for roles like this, each with
        a concrete next step (a specific small project, certification, or
        practice) — not generic advice like "keep learning".

        score_ceiling: one or two sentences on why the score stopped where it
        did — what no amount of rewording could change.
        """;

    public async Task<ResumeReviewDraft> ReviewAsync(
        string finalResumeText,
        int baselineScore,
        int finalScore,
        int targetScore,
        TailorContext context,
        CancellationToken cancellationToken = default)
    {
        var userPrompt = $"""
            Role: {context.RoleTitle}
            Company: {context.CompanyName}

            <job_description>
            {context.JobDescription}
            </job_description>

            <tailored_resume>
            {finalResumeText}
            </tailored_resume>

            <extra_facts>
            {(string.IsNullOrWhiteSpace(context.ExtraFacts) ? "(none provided)" : context.ExtraFacts)}
            </extra_facts>

            Match score before tailoring: {baselineScore}. After tailoring: {finalScore}. The bar for "ready to apply" is {targetScore}.
            """;

        var schema = SchemaObject(new
        {
            reality_check = SchemaString("2-4 blunt sentences; the first is the bottom line."),
            score_ceiling = SchemaString("Why the score stopped where it did."),
            dealbreakers = SchemaArray(SchemaObject(new
            {
                requirement = SchemaString("The hard requirement, in the posting's words."),
                why = SchemaString("Why the resume doesn't meet it."),
            }, "requirement", "why"), "Hard requirements clearly unmet. Often empty."),
            strengths = SchemaArray(SchemaObject(new
            {
                point = SchemaString("The strength, relative to this posting."),
                evidence = SchemaString("A verbatim quote from the resume that proves it."),
            }, "point", "evidence"), "What makes the candidate competitive here."),
            gaps = SchemaArray(SchemaObject(new
            {
                requirement = SchemaString("What the posting wants."),
                severity = SchemaEnum("How much it matters.", "dealbreaker", "fixable", "minor"),
                fix = SchemaEnum("How it could close.", "resume", "interview", "build_skill"),
                advice = SchemaString("One or two concrete sentences."),
            }, "requirement", "severity", "fix", "advice"), "Shortfalls, most important first."),
            work_on = SchemaArray(SchemaObject(new
            {
                skill = SchemaString("The skill or credential."),
                why = SchemaString("Why it matters for roles like this."),
                next_step = SchemaString("A concrete next step."),
            }, "skill", "why", "next_step"), "2-4 things worth investing in."),
        }, "reality_check", "score_ceiling", "dealbreakers", "strengths", "gaps", "work_on");

        var payload = await caller.CallAsync<ReviewPayload>(
            "writing your reality check", SystemPrompt, userPrompt, schema, cancellationToken);

        return new ResumeReviewDraft(
            payload.RealityCheck,
            payload.ScoreCeiling,
            payload.Dealbreakers.Select(d => new Dealbreaker(d.Requirement, d.Why)).ToList(),
            payload.Strengths.Select(s => new Strength(s.Point, s.Evidence)).ToList(),
            payload.Gaps.Select(g => new Gap(g.Requirement, ParseSeverity(g.Severity), ParseFix(g.Fix), g.Advice)).ToList(),
            payload.WorkOn.Select(w => new WorkOnItem(w.Skill, w.Why, w.NextStep)).ToList());
    }

    private static GapSeverity ParseSeverity(string value) => value switch
    {
        "dealbreaker" => GapSeverity.Dealbreaker,
        "minor" => GapSeverity.Minor,
        _ => GapSeverity.Fixable,
    };

    private static GapFix ParseFix(string value) => value switch
    {
        "resume" => GapFix.Resume,
        "interview" => GapFix.Interview,
        _ => GapFix.BuildSkill,
    };

    private sealed record ReviewPayload(
        string RealityCheck,
        string ScoreCeiling,
        List<DealbreakerPayload> Dealbreakers,
        List<StrengthPayload> Strengths,
        List<GapPayload> Gaps,
        List<WorkOnPayload> WorkOn);

    private sealed record DealbreakerPayload(string Requirement, string Why);

    private sealed record StrengthPayload(string Point, string Evidence);

    private sealed record GapPayload(string Requirement, string Severity, string Fix, string Advice);

    private sealed record WorkOnPayload(string Skill, string Why, string NextStep);
}
