using CareerConnect.Api.Domain;
using static CareerConnect.Api.Services.ClaudeStructuredCaller;

namespace CareerConnect.Api.Services;

/// <summary>
/// A second, separate read of the finished rewrite whose only job is to
/// catch overreach. The tailorer is told not to embellish, but it is also
/// chasing a score; a checker with no score to chase is the better judge.
/// </summary>
public class ClaudeResumeClaimsAuditor(ClaudeStructuredCaller caller) : IResumeClaimsAuditor
{
    private const string SystemPrompt = """
        You are a strict fact-checker for resumes. A candidate's resume lines
        were rewritten to target a job posting. Your job is to catch any
        rewritten line that claims something the candidate hasn't shown.

        Evidence is ONLY the original resume and the candidate's extra facts.
        The job posting is not evidence.

        Flag a changed line if its new text claims anything not supported by
        the evidence: a technology, tool, language, framework, platform or
        certification; a number, metric or scale; a responsibility, ownership
        or leadership level; seniority; an outcome or impact; or an
        accomplishment that belongs to a different job or project than the
        line it's now on.

        Do not flag rewording, reordering, dropped content, or standard
        synonyms for the same thing ("REST APIs" for "RESTful API
        development", "C#/.NET" for "C#, .NET Core"). A skill listed in the
        original skills section counts as shown. Be strict about substance,
        not pedantic about phrasing.

        Return only the lines that fail, each with a short reason naming the
        unsupported claim. An empty list is the right answer when every line
        holds up.
        """;

    public async Task<List<UnsupportedClaim>> AuditAsync(
        ResumeLayout baseLayout,
        string? extraFacts,
        IReadOnlyList<ResumeChange> changes,
        CancellationToken cancellationToken = default)
    {
        if (changes.Count == 0)
        {
            return [];
        }

        var userPrompt = $"""
            <original_resume>
            {baseLayout.ToPlainText()}
            </original_resume>

            <extra_facts>
            {(string.IsNullOrWhiteSpace(extraFacts) ? "(none provided)" : extraFacts)}
            </extra_facts>

            <changed_lines>
            {string.Join("\n\n", changes.Select(c => $"{c.LineId}\n  ORIGINAL: {c.Before}\n  NEW: {c.After}"))}
            </changed_lines>
            """;

        var schema = SchemaObject(new
        {
            unsupported = SchemaArray(SchemaObject(new
            {
                line_id = SchemaString("Id of a changed line whose new text isn't supported."),
                reason = SchemaString("The unsupported claim, in a few words, e.g. \"adds Kubernetes, never shown\"."),
            }, "line_id", "reason"), "Changed lines that claim something the evidence doesn't support."),
        }, "unsupported");

        var payload = await caller.CallAsync<AuditPayload>(
            "checking the rewrite against your real experience", SystemPrompt, userPrompt, schema, cancellationToken);

        return payload.Unsupported.Select(u => new UnsupportedClaim(u.LineId, u.Reason)).ToList();
    }

    private sealed record AuditPayload(List<ClaimPayload> Unsupported);

    private sealed record ClaimPayload(string LineId, string Reason);
}
