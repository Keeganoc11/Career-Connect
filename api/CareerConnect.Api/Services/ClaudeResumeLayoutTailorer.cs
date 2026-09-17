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
        stays where it is, and each line you edit must fill exactly the space it
        had — one printed row, or two or three for a bullet that wraps.

        You will see the resume as numbered lines. Some are marked editable:
        bullet points (you replace the text after the bullet) and skill lines
        (you replace the list after the fixed category label). Everything
        else — name, contact details, headings, job titles, companies, dates,
        education — is locked and not yours to change.

        Hard rules for every edit:
        - Only edit lines marked editable, and replace that line's whole editable text.
        - Stay within the line's character range. It is a real limit: longer text runs off the page, and
          shorter text on a multi-row bullet leaves an empty row.
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

    private const string SwapRules = """

        ENTRY SWAPS. When entry slots are listed, you may also fill a slot with a different job or
        project described in the extra facts — the candidate can't fit everything on one page, and
        the extra facts hold what didn't make it.

        - Swap only when the entry from the extra facts is clearly more relevant to this posting than
          the one on the page. When in doubt, don't swap. Never swap in something already on the page.
        - A project goes into a project slot, a job into a work slot.
        - Fill every heading field and every bullet of the slot, no more and no fewer. Each heading
          field keeps its role: the bold field is the name or job title, the dates field is dates,
          the rest are company and location, laid out the way the slot's current fields are.
        - Take names, job titles, companies, places and dates exactly as the extra facts state them.
          Match the slot's date style (e.g. "Aug 2026 - Present" if the slot abbreviates months).
          If the extra facts don't give dates for an entry, it can't be swapped in.
        - A job swapped into a work slot must keep the work section newest-first.
        - Bullets must be supported by that entry's own facts only, within each bullet's character
          range. The honesty rules above apply in full.
        - Give a one-sentence reason addressed to the candidate for the swap.
        """;

    private const string FitPrompt = """
        Each resume line below doesn't fill its space on the page exactly. A
        line marked TOO LONG runs past its rows: tighten it, cutting filler
        words first. A line marked TOO SHORT leaves one of its rows empty:
        expand it by making the same facts more specific in the posting's
        language. Land every line inside its character range. Keep its
        meaning and the key terms that match the job posting. Never add a new
        claim, tool, or number. Plain text only, no bullet symbol. Return
        every line you were given.
        """;

    public bool IsConfigured => caller.IsConfigured;

    public async Task<TailorProposal> TailorAsync(
        ResumeLayout current,
        ResumeLayout baseLayout,
        IReadOnlyDictionary<string, CharacterRange> characterBudgets,
        MatchAnalysis latestScore,
        TailorContext context,
        string? instructions = null,
        bool allowSwaps = false,
        CancellationToken cancellationToken = default)
    {
        allowSwaps = allowSwaps && !string.IsNullOrWhiteSpace(context.ExtraFacts);
        var resume = new StringBuilder();
        foreach (var line in current.Lines.Where(l => l.Kind != ResumeLineKind.Blank))
        {
            if (!line.Editable)
            {
                resume.AppendLine($"{line.Id} [locked] {line.Text}");
                continue;
            }

            var budget = characterBudgets.GetValueOrDefault(line.Id, new CharacterRange(1, 80));
            var size = line.RowCount == 1
                ? $"max {budget.Max} chars"
                : $"wraps onto {line.RowCount} rows: {budget.Min}-{budget.Max} chars";
            var label = line.Kind == ResumeLineKind.Skill
                ? $"[editable skill line, fixed label \"{line.Prefix.Trim()}\", {size}]"
                : $"[editable bullet, {size}]";
            resume.AppendLine($"{line.Id} {label} {line.EditableText}");

            var original = baseLayout.Find(line.Id)?.EditableText;
            if (original is not null && original != line.EditableText)
            {
                resume.AppendLine($"    ORIGINAL: {original}");
            }
        }

        var slots = new StringBuilder();
        if (allowSwaps)
        {
            foreach (var entry in ResumeEntries.Find(current))
            {
                slots.AppendLine($"SLOT {entry.SlotId} ({(entry.Section == EntrySection.Work ? "work" : "project")})");
                foreach (var id in entry.HeaderLineIds)
                {
                    var fields = ResumeEntries.Fields(current.Find(id)!)
                        .Select(f => $"[{(f.Bold ? "bold" : ResumeEntries.DateRange(f.Text) is not null ? "dates" : f.Italic ? "italic" : "plain")}] \"{f.Text.Trim()}\"");
                    slots.AppendLine($"  heading {id}: {string.Join(" | ", fields)}");
                }
                foreach (var id in entry.BulletLineIds)
                {
                    var line = current.Find(id)!;
                    var budget = characterBudgets.GetValueOrDefault(id, new CharacterRange(1, 80));
                    var size = line.RowCount == 1 ? $"max {budget.Max} chars" : $"{line.RowCount} rows: {budget.Min}-{budget.Max} chars";
                    slots.AppendLine($"  bullet {id} ({size}): {line.Text}");
                }
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

            {(allowSwaps ? $"<entry_slots>\n{slots}</entry_slots>\n" : "")}
            {request}
            Rewrite the editable lines that would make this resume a stronger, honest fit for this posting{(allowSwaps ? ", and swap in an entry from the extra facts only where it's clearly the better fit" : "")}.
            """;

        var edits = SchemaArray(SchemaObject(new
        {
            line_id = SchemaString("Id of an editable line, e.g. \"L15\"."),
            text = SchemaString("The complete new editable text for that line, within its character range."),
            reason = SchemaString("One sentence, addressed to the candidate, on why this change helps."),
        }, "line_id", "text", "reason"), "Replacements for the lines worth changing. Omit lines to leave them as they are.");

        var schema = allowSwaps
            ? SchemaObject(new
            {
                edits,
                swaps = SchemaArray(SchemaObject(new
                {
                    slot_id = SchemaString("The slot being filled, e.g. \"S33\"."),
                    source_name = SchemaString("The swapped-in entry's name as the extra facts give it."),
                    headings = SchemaArray(SchemaObject(new
                    {
                        line_id = SchemaString("A heading line id of that slot."),
                        fields = SchemaArray(SchemaString("One field's new text."), "New text for each field of that heading line, in order."),
                    }, "line_id", "fields"), "Every heading line of the slot, in order."),
                    bullets = SchemaArray(SchemaObject(new
                    {
                        line_id = SchemaString("A bullet line id of that slot."),
                        text = SchemaString("The new bullet, within its character range."),
                    }, "line_id", "text"), "Every bullet of the slot, in order."),
                    reason = SchemaString("One sentence, addressed to the candidate, on why this entry fits the posting better."),
                }, "slot_id", "source_name", "headings", "bullets", "reason"), "Entry swaps. Usually empty."),
            }, "edits", "swaps")
            : SchemaObject(new { edits }, "edits");

        var payload = await caller.CallAsync<ProposalPayload>(
            "rewriting your resume", allowSwaps ? SystemPrompt + SwapRules : SystemPrompt, userPrompt, schema, cancellationToken);

        return new TailorProposal(
            payload.Edits.Select(e => new LineEdit(e.LineId, e.Text, e.Reason)).ToList(),
            (payload.Swaps ?? [])
                .Select(sw => new EntrySwapProposal(
                    sw.SlotId,
                    sw.SourceName,
                    sw.Headings.Select(h => new HeaderLineFields(h.LineId, h.Fields)).ToList(),
                    sw.Bullets.Select(b => new LineEdit(b.LineId, b.Text, sw.Reason)).ToList(),
                    sw.Reason))
                .ToList());
    }

    public async Task<List<LineEdit>> FitAsync(
        IReadOnlyList<LineToFit> lines,
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
            {string.Join("\n", lines.Select(l => $"{l.LineId} [{(l.TooShort ? "TOO SHORT" : "TOO LONG")}, {l.Rows} row(s), {l.MinCharacters}-{l.MaxCharacters} chars, currently {l.Text.Length}] {l.Text}"))}
            </lines>
            """;

        var schema = SchemaObject(new
        {
            edits = SchemaArray(SchemaObject(new
            {
                line_id = SchemaString("Id of the line being adjusted."),
                text = SchemaString("The adjusted text, within its character range."),
                reason = SchemaString("Leave empty."),
            }, "line_id", "text", "reason"), "One entry per line given."),
        }, "edits");

        var payload = await caller.CallAsync<EditsPayload>(
            "fitting a rewritten line into its space", FitPrompt, userPrompt, schema, cancellationToken);

        return payload.Edits.Select(e => new LineEdit(e.LineId, e.Text, e.Reason)).ToList();
    }

    private sealed record EditsPayload(List<EditPayload> Edits);

    private sealed record ProposalPayload(List<EditPayload> Edits, List<SwapPayload>? Swaps);

    private sealed record SwapPayload(
        string SlotId, string SourceName, List<HeadingPayload> Headings, List<SwapBulletPayload> Bullets, string Reason);

    private sealed record HeadingPayload(string LineId, List<string> Fields);

    private sealed record SwapBulletPayload(string LineId, string Text);

    private sealed record EditPayload(string LineId, string Text, string Reason);
}
