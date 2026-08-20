using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;

namespace CareerConnect.Api.Services;

/// <summary>
/// Reads a snapshot of the candidate's whole pipeline and surfaces what
/// actually needs attention — using structured outputs so the response is a
/// fixed shape rather than free-text the UI has to parse.
/// </summary>
public class ClaudeCopilotAnalyzer : ICopilotAnalyzer
{
    private const string SystemPrompt = """
        You are a career coach reviewing a job seeker's application pipeline
        to tell them what actually needs their attention right now.

        You'll get each tracked application's company, role, status, date
        applied, days since last activity, and latest resume match score (if
        any's been run). Rejected and Withdrawn applications are excluded —
        they're done, no action needed.

        Write a short, honest overall_summary (2-3 sentences) of where things
        stand: pipeline size, response rate, anything that stands out.

        Then list concrete actions, most important first. Good candidates for
        an action:
        - An application stuck at "Applied" for 3+ weeks with no movement —
          suggest following up or treating it as likely gone quiet.
        - A low match score (below ~50) on an application that's still early
          (Applied or PhoneScreen) — suggest tailoring the resume before
          investing more time, or before an upcoming interview.
        - No active resume, or an application never scored against one —
          suggest fixing that first, since it blocks everything else.
        - An Interview or Offer stage application with old last-activity —
          time-sensitive stages going quiet deserve a nudge.

        Skip generic advice ("keep applying") and applications that are
        already in good shape. If the pipeline is small, healthy, or empty,
        say so plainly rather than inventing actions to fill the list — an
        empty or short action list is a fine, honest answer.

        Each action needs: a short title, one sentence of detail explaining
        why, a priority (high/medium/low), and — when it's about one specific
        application — that application's index from the list. General advice
        not tied to one application (e.g. "add a resume") omits the index.
        """;

    private readonly AnthropicClient? _client;
    private readonly string _model;
    private readonly Effort _effort;

    public ClaudeCopilotAnalyzer(IConfiguration configuration)
    {
        _model = AnthropicClientFactory.ResolveModel(configuration);
        _effort = AnthropicClientFactory.ResolveEffort(configuration);
        _client = AnthropicClientFactory.CreateClient(configuration);
    }

    public bool IsConfigured => _client is not null;

    public async Task<CopilotInsights> AnalyzeAsync(
        CopilotPipelineSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (_client is null)
        {
            throw new CopilotAnalysisException("No Anthropic API key is configured.");
        }

        if (snapshot.Applications.Count == 0)
        {
            return new CopilotInsights(
                "No applications tracked yet — add one to get started.", []);
        }

        var appLines = string.Join("\n", snapshot.Applications.Select(a =>
        {
            var daysSinceActivity = (int)(snapshot.NowUtc - a.LastActivityUtc).TotalDays;
            var scoreText = a.LatestMatchScore.HasValue ? a.LatestMatchScore.Value.ToString() : "not yet scored";
            return $"[{a.Index}] {a.CompanyName} — {a.RoleTitle} | status: {a.Status} | " +
                   $"applied {a.DateApplied:yyyy-MM-dd} | last activity {daysSinceActivity} days ago | match score: {scoreText}";
        }));

        var userPrompt = $"""
            Active resume: {(snapshot.HasActiveResume ? "yes" : "no")}
            Today's date: {snapshot.NowUtc:yyyy-MM-dd}

            Applications:
            {appLines}
            """;

        Message response;
        try
        {
            response = await _client.Messages.Create(new MessageCreateParams
            {
                Model = _model,
                MaxTokens = 4000,
                System = SystemPrompt,
                OutputConfig = new OutputConfig
                {
                    Effort = _effort,
                    Format = new JsonOutputFormat { Schema = ResponseSchema },
                },
                Messages = [new() { Role = Role.User, Content = userPrompt }],
            });
        }
        catch (Exception ex)
        {
            throw new CopilotAnalysisException("The insights request to Claude failed.", ex);
        }

        if (response.StopReason == "refusal" || response.StopReason == "max_tokens")
        {
            throw new CopilotAnalysisException("Claude couldn't generate insights for this pipeline right now.");
        }

        var json = AnthropicResponse.ExtractText(response);
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new CopilotAnalysisException("Claude returned an empty response.");
        }

        try
        {
            var payload = JsonSerializer.Deserialize<InsightsPayload>(json, AnthropicResponse.SnakeCaseJsonOptions)
                ?? throw new CopilotAnalysisException("Claude returned an empty analysis.");

            return new CopilotInsights(
                payload.OverallSummary,
                payload.Actions
                    .Select(a => new CopilotAction(a.Title, a.Detail, a.Priority, a.ApplicationIndex))
                    .ToList());
        }
        catch (JsonException ex)
        {
            throw new CopilotAnalysisException("Claude returned an analysis that could not be read.", ex);
        }
    }

    private sealed record InsightsPayload(string OverallSummary, List<ActionPayload> Actions);

    private sealed record ActionPayload(string Title, string Detail, string Priority, int? ApplicationIndex);

    private static Dictionary<string, JsonElement> ResponseSchema => new()
    {
        ["type"] = JsonSerializer.SerializeToElement("object"),
        ["properties"] = JsonSerializer.SerializeToElement(new
        {
            overall_summary = new
            {
                type = "string",
                description = "2-3 honest sentences on the pipeline's overall state, addressed to the candidate.",
            },
            actions = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        title = new { type = "string", description = "Short action title, e.g. 'Follow up with Acme'." },
                        detail = new { type = "string", description = "One sentence explaining why this matters now." },
                        priority = new { type = "string", @enum = new[] { "high", "medium", "low" } },
                        application_index = new
                        {
                            type = new[] { "integer", "null" },
                            description = "The [N] index of the specific application this is about, or null for general advice.",
                        },
                    },
                    required = new[] { "title", "detail", "priority", "application_index" },
                    additionalProperties = false,
                },
                description = "Concrete next actions, most important first. Empty is a valid, honest answer for a small or healthy pipeline.",
            },
        }),
        ["required"] = JsonSerializer.SerializeToElement(new[] { "overall_summary", "actions" }),
        ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
    };
}
