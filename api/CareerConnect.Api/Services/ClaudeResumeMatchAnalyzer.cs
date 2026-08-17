using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using CareerConnect.Api.Domain;

namespace CareerConnect.Api.Services;

/// <summary>
/// Scores a resume against a job description with the Claude API, using
/// structured outputs so the response is guaranteed to match the schema below
/// (no prompt-and-pray JSON parsing).
/// </summary>
public class ClaudeResumeMatchAnalyzer : IResumeMatchAnalyzer
{
    private const string SystemPrompt = """
        You are an experienced technical recruiter reviewing a candidate's resume
        against a specific job posting. Assess only what the resume actually
        evidences — never assume unstated experience.

        Scoring guide (0-100): how well this resume, as written, would fare with a
        recruiter screening for this specific role.
          85-100  Strong match; clears the bar on nearly every stated requirement.
          70-84   Good match; meets the core requirements, gaps are secondary.
          50-69   Partial match; meets some requirements, missing others that matter.
          30-49   Weak match; a few transferable signals, most requirements unmet.
          0-29    Not a realistic fit as written.

        Be specific and honest. An inflated score is useless to the candidate.
        Keywords should be short phrases lifted from the posting's requirements
        ("ASP.NET Core", "CI/CD pipelines"), not full sentences.

        For suggestions, give 3-6 concrete edits ordered by impact. Each one names
        the resume section it applies to, a one-sentence reason, and ready-to-paste
        text written in first person, resume style (not a description of what to
        write — the actual sentence or bullet).

        The text you provide must never invent specifics the candidate hasn't
        stated: no fabricated metrics, employers, technologies, or outcomes. Two
        kinds of suggestion are legitimate:
          1. Reframing existing content — pull a truthfully-stated fact into
             stronger, more relevant phrasing. Give the full finished text.
          2. Flagging a real gap that needs content only the candidate has —
             give a template with bracketed placeholders for the missing
             specifics, e.g. "Built [describe the system] using ASP.NET Core
             and EF Core, which [specific outcome you can quantify]." Never
             fill a bracket with an invented number or claim.
        """;

    private readonly AnthropicClient? _client;
    private readonly string _model;
    private readonly Effort _effort;
    private readonly ILogger<ClaudeResumeMatchAnalyzer> _logger;

    public ClaudeResumeMatchAnalyzer(IConfiguration configuration, ILogger<ClaudeResumeMatchAnalyzer> logger)
    {
        _logger = logger;
        _model = AnthropicClientFactory.ResolveModel(configuration);
        _effort = ParseEffort(configuration["Anthropic:Effort"]);
        _client = AnthropicClientFactory.CreateClient(configuration);

        if (_client is null)
        {
            _logger.LogWarning(
                "No Anthropic API key configured — match scoring is disabled. " +
                "Set ANTHROPIC_API_KEY or the Anthropic:ApiKey user secret to enable it.");
        }
    }

    public bool IsConfigured => _client is not null;

    public async Task<MatchAnalysis> AnalyzeAsync(
        string resumeText,
        string jobDescriptionText,
        string roleTitle,
        string companyName,
        CancellationToken cancellationToken = default)
    {
        if (_client is null)
        {
            throw new MatchAnalysisException("No Anthropic API key is configured.");
        }

        var userPrompt = $"""
            Role: {roleTitle}
            Company: {companyName}

            <job_description>
            {jobDescriptionText}
            </job_description>

            <resume>
            {resumeText}
            </resume>

            Score this resume against this posting.
            """;

        Message response;
        try
        {
            response = await _client.Messages.Create(new MessageCreateParams
            {
                Model = _model,
                // Generous ceiling: on thinking-enabled models MaxTokens caps
                // thinking plus the response, so a tight budget truncates.
                MaxTokens = 8000,
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
            _logger.LogError(ex, "Anthropic request failed while scoring a resume.");
            throw new MatchAnalysisException("The scoring request to Claude failed.", ex);
        }

        // Safety classifiers can decline with HTTP 200 — check before reading content.
        if (response.StopReason == "refusal")
        {
            throw new MatchAnalysisException(
                "Claude declined to analyze this content. Check the job description for anything unexpected.");
        }

        if (response.StopReason == "max_tokens")
        {
            throw new MatchAnalysisException("The analysis was cut off before it finished. Try a shorter job description.");
        }

        var json = AnthropicResponse.ExtractText(response);

        if (string.IsNullOrWhiteSpace(json))
        {
            throw new MatchAnalysisException("Claude returned an empty response.");
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<AnalysisPayload>(json, AnthropicResponse.SnakeCaseJsonOptions)
                ?? throw new MatchAnalysisException("Claude returned an empty analysis.");

            return new MatchAnalysis(
                Score: Math.Clamp(parsed.Score, 0, 100),
                Summary: parsed.Summary,
                MatchedKeywords: parsed.MatchedKeywords,
                MissingKeywords: parsed.MissingKeywords,
                Suggestions: parsed.Suggestions,
                ModelId: response.Model ?? _model);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Could not parse the analysis payload from Claude.");
            throw new MatchAnalysisException("Claude returned an analysis that could not be read.", ex);
        }
    }

    private sealed record AnalysisPayload(
        int Score,
        string Summary,
        List<string> MatchedKeywords,
        List<string> MissingKeywords,
        List<SuggestedEdit> Suggestions);

    private static Effort ParseEffort(string? configured) => configured?.ToLowerInvariant() switch
    {
        "low" => Effort.Low,
        "high" => Effort.High,
        "max" => Effort.Max,
        // Scoped extraction-and-judgement task: medium is the cost/quality
        // sweet spot here. Override with Anthropic:Effort.
        _ => Effort.Medium,
    };

    private static Dictionary<string, JsonElement> ResponseSchema => new()
    {
        ["type"] = JsonSerializer.SerializeToElement("object"),
        ["properties"] = JsonSerializer.SerializeToElement(new
        {
            score = new
            {
                type = "integer",
                description = "0-100 match score, per the scoring guide.",
            },
            summary = new
            {
                type = "string",
                description = "Two or three sentences explaining the score, addressed to the candidate.",
            },
            matched_keywords = new
            {
                type = "array",
                items = new { type = "string" },
                description = "Short requirement phrases from the posting that the resume already evidences.",
            },
            missing_keywords = new
            {
                type = "array",
                items = new { type = "string" },
                description = "Short requirement phrases from the posting that the resume does not evidence.",
            },
            suggestions = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        section = new
                        {
                            type = "string",
                            description = "Where this applies, e.g. 'Skills', 'Professional Summary', 'Projects — <name>', 'New bullet under <Company> role'.",
                        },
                        guidance = new
                        {
                            type = "string",
                            description = "One sentence on why this change helps, addressed to the candidate.",
                        },
                        suggested_text = new
                        {
                            type = "string",
                            description = "Ready-to-paste replacement or addition, written in first-person resume style — the actual sentence, not a description of one. Use bracketed placeholders like [describe the specific outcome] for any real detail the candidate must supply; never invent specific achievements, metrics, employers, or technologies not already stated.",
                        },
                    },
                    required = new[] { "section", "guidance", "suggested_text" },
                    additionalProperties = false,
                },
                description = "3-6 concrete, copy-pasteable resume edits ordered by impact.",
            },
        }),
        ["required"] = JsonSerializer.SerializeToElement(
            new[] { "score", "summary", "matched_keywords", "missing_keywords", "suggestions" }),
        ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
    };
}
