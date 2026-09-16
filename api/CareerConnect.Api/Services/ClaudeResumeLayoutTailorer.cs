using System.Text;
using CareerConnect.Api.Domain;
using static CareerConnect.Api.Services.ClaudeStructuredCaller;

namespace CareerConnect.Api.Services;

/// <summary>
/// Rewrites the editable lines of a positioned resume toward a posting. It
/// sees the whole page for context but can only answer with replacements for
/// line ids it was told are editable, each within a character budget — the
/// format itself is never something it gets to touch.
/// </summary>
public class ClaudeResumeLayoutTailorer(ClaudeStructuredCaller caller) : IResumeLayoutTailorer
{
    private const string SystemPrompt = """
        You tailor a candidate's one-page resume to a specific job posting by
        changing words, and only words. The page layout is fixed: every line
        stays where it is, and each line you edit must still fit on one line.

        You will see the resume as numbered lines. Some are marked editable:
        bullet points (you replace the text after the bullet) and skill lines
        (you replace the list after the fixed category label). Everything
        else — name, contact details, headings, job titles, companies, dates,
        education — is locked and not yours to change.

        Hard rules for every edit:
        - Only edit lines marked editable, and replace that line's whole editable text.
        - Stay within the line's character limit. It is a real limit: longer text doesn't fit on the page.
        - Plain text only: no markdown, no line breaks, no leading bullet symbol, no [bracketed placeholders].

        Honesty rules — these matter more than the score:
        - The ORIGINAL line text is ground truth for what the candidate did. The extra facts, if any, are also true.
        - You may reword toward the posting's own terminology when the underlying fact supports it,
          reorder items, sharpen vague phrasing, and change which aspect of a real accomplishment leads.
        - On skill lines you may reorder, drop less relevant items, and bring in skills the candidate
          demonstrably has elsewhere on the resume or in the extra facts.
        - You may not add a technology, tool, language, framework, or certification the candidate hasn't shown.
        - You may not invent or change any number, and you may not inflate scope, scale, seniority,
          or ownership ("led", "architected", "owned") beyond what the original says.
        - A bullet belongs to its job or project. Don't move an accomplishment from one entry to another.
          An extra fact may only go into the entry it's about, or into skills.
        - Coursework or a side project is not production experience; don't word it as if it were.

        Strategy: use the latest score's missing keywords and suggestions, but only where the candidate
        honestly has the thing. Prefer the posting's exact phrasing for what they really have. If a line
        is already well aligned, leave it alone — don't change lines for the sake of changing them.

        For each edit, give a one-sentence reason addressed to the candidate ("Leads with REST API work,
        which the posting lists first").
        """;

    private const string ShortenPrompt = """
        Each resume line below is too long to fit on one line of the page.
        Shorten each one to at most its character limit. Keep its meaning and
        the key terms that match the job posting; cut filler words first. Do
        not add anything new, and never invent or change a number. Plain text
        only, no bullet symbol. Return every line you were given.
        """;

    public bool IsConfigured => caller.IsConfigured;

    public async Task<List<LineEdit>> TailorAsync(
        ResumeLayout current,
        ResumeLayout baseLayout,
        IReadOnlyDictionary<string, int> characterBudgets,
        MatchAnalysis latestScore,
        TailorContext context,
        string? instructions = null,
        CancellationToken cancellationToken = default)
    {
        var resume = new StringBuilder();
        foreach (var line in current.Lines.Where(l => l.Kind != ResumeLineKind.Blank))
        {
            if (!line.Editable)
            {
                resume.AppendLine($"{line.Id} [locked] {line.Text}");
                continue;
            }

            var budget = characterBudgets.GetValueOrDefault(line.Id);
            var label = line.Kind == ResumeLineKind.Skill
                ? $"[editable skill line, fixed label \"{string.Concat(line.Runs.Take(line.EditableFrom!.Value).Select(r => r.Text)).Trim()}\", max {budget} chars]"
                : $"[editable bullet, max {budget} chars]";
            resume.AppendLine($"{line.Id} {label} {line.EditableText}");

            var original = baseLayout.Find(line.Id)?.EditableText;
            if (original is not null && original != line.EditableText)
            {
                resume.AppendLine($"    ORIGINAL: {original}");
            }
        }

        var request = string.IsNullOrWhiteSpace(instructions)
            ? ""
            : $"<candidate_request>\n{instructions.Trim()}\n</candidate_request>\n\n" +
              "The candidate asked for this version specifically. Follow the request as far as the rules allow — " +
              "the format and honesty rules win if they conflict — and make each reason say how the edit serves it.\n";

        var userPrompt = $"""
            Role: {context.RoleTitle}
            Company: {context.CompanyName}

            <job_description>
            {context.JobDescription}
            </job_description>

            <resume_lines>
            {resume}
            </resume_lines>

            <extra_facts>
            {(string.IsNullOrWhiteSpace(context.ExtraFacts) ? "(none provided)" : context.ExtraFacts)}
            </extra_facts>

            <latest_score score="{latestScore.Score}">
            Summary: {latestScore.Summary}
            Missing: {string.Join(", ", latestScore.MissingKeywords)}
            Suggestions:
            {string.Join("\n", latestScore.Suggestions.Select(s => $"- {s.Section}: {s.Guidance}"))}
            </latest_score>

            {request}
            Rewrite the editable lines that would make this resume a stronger, honest fit for this posting.
            """;

        var schema = SchemaObject(new
        {
            edits = SchemaArray(SchemaObject(new
            {
                line_id = SchemaString("Id of an editable line, e.g. \"L15\"."),
                text = SchemaString("The complete new editable text for that line, within its character limit."),
                reason = SchemaString("One sentence, addressed to the candidate, on why this change helps."),
            }, "line_id", "text", "reason"), "Replacements for the lines worth changing. Omit lines to leave them as they are."),
        }, "edits");

        var payload = await caller.CallAsync<EditsPayload>(
            "rewriting your resume", SystemPrompt, userPrompt, schema, cancellationToken);

        return payload.Edits.Select(e => new LineEdit(e.LineId, e.Text, e.Reason)).ToList();
    }

    public async Task<List<LineEdit>> ShortenAsync(
        IReadOnlyList<LineToShorten> lines,
        TailorContext context,
        CancellationToken cancellationToken = default)
    {
        if (lines.Count == 0)
        {
            return [];
        }

        var userPrompt = $"""
            Role: {context.RoleTitle} at {context.CompanyName}

            <lines>
            {string.Join("\n", lines.Select(l => $"{l.LineId} [max {l.MaxCharacters} chars, currently {l.Text.Length}] {l.Text}"))}
            </lines>
            """;

        var schema = SchemaObject(new
        {
            edits = SchemaArray(SchemaObject(new
            {
                line_id = SchemaString("Id of the line being shortened."),
                text = SchemaString("The shortened text, within the limit."),
                reason = SchemaString("Leave empty."),
            }, "line_id", "text", "reason"), "One entry per line given."),
        }, "edits");

        var payload = await caller.CallAsync<EditsPayload>(
            "fitting a rewritten line on one line", ShortenPrompt, userPrompt, schema, cancellationToken);

        return payload.Edits.Select(e => new LineEdit(e.LineId, e.Text, e.Reason)).ToList();
    }

    private sealed record EditsPayload(List<EditPayload> Edits);

    private sealed record EditPayload(string LineId, string Text, string Reason);
}
