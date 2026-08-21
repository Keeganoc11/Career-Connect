using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;

namespace CareerConnect.Api.Services;

/// <summary>
/// Generates interview prep grounded in the candidate's actual resume and
/// the posting's requirements — using structured outputs so the response is
/// a fixed shape rather than free-text the UI has to parse.
/// </summary>
public class ClaudeInterviewPrepGenerator : IInterviewPrepGenerator
{
    private const string SystemPrompt = """
        You are helping a job seeker prepare for an interview for a specific
        role, based on their actual resume and the job posting.

        Generate two things:

        1. QUESTIONS — likely interview questions: a mix of role-specific
           technical/behavioral questions this posting's requirements
           suggest, and questions specifically probing gaps between the
           resume and the posting (an interviewer who read this resume
           against this posting would reasonably ask about those gaps). For
           each, give one sentence on why it might come up.

        2. TALKING POINTS — specific things from the resume worth proactively
           highlighting because they map well to what this posting cares
           about. Ground every point in something the resume actually
           states — never invent an achievement to suggest as a talking
           point. For each, one sentence on how to use it (when/how to bring
           it up).

        Be concrete and specific to this resume and this posting, not
        generic interview advice that would apply to any candidate.
        """;

    private readonly AnthropicClient? _client;
    private readonly string _model;
    private readonly Effort _effort;

    public ClaudeInterviewPrepGenerator(IConfiguration configuration)
    {
        _model = AnthropicClientFactory.ResolveModel(configuration);
        _effort = AnthropicClientFactory.ResolveEffort(configuration);
        _client = AnthropicClientFactory.CreateClient(configuration);
    }

    public bool IsConfigured => _client is not null;

    public async Task<InterviewPrep> GenerateAsync(
        string resumeText,
        string jobDescriptionText,
        string roleTitle,
        string companyName,
        CancellationToken cancellationToken = default)
    {
        if (_client is null)
        {
            throw new InterviewPrepGenerationException("No Anthropic API key is configured.");
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

            Generate interview prep for this application.
            """;

        Message response;
        try
        {
            response = await _client.Messages.Create(new MessageCreateParams
            {
                Model = _model,
                MaxTokens = 6000,
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
            throw new InterviewPrepGenerationException("The interview prep request to Claude failed.", ex);
        }

        if (response.StopReason == "refusal" || response.StopReason == "max_tokens")
        {
            throw new InterviewPrepGenerationException("Claude couldn't generate interview prep for this application right now.");
        }

        var json = AnthropicResponse.ExtractText(response);
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InterviewPrepGenerationException("Claude returned an empty response.");
        }

        try
        {
            var payload = JsonSerializer.Deserialize<PrepPayload>(json, AnthropicResponse.SnakeCaseJsonOptions)
                ?? throw new InterviewPrepGenerationException("Claude returned an empty result.");

            return new InterviewPrep(
                payload.Questions.Select(q => new InterviewQuestion(q.Question, q.WhyItMightComeUp)).ToList(),
                payload.TalkingPoints.Select(t => new TalkingPoint(t.Point, t.HowToUseIt)).ToList());
        }
        catch (JsonException ex)
        {
            throw new InterviewPrepGenerationException("Claude returned a result that could not be read.", ex);
        }
    }

    private sealed record PrepPayload(List<QuestionPayload> Questions, List<TalkingPointPayload> TalkingPoints);

    private sealed record QuestionPayload(string Question, string WhyItMightComeUp);

    private sealed record TalkingPointPayload(string Point, string HowToUseIt);

    private static Dictionary<string, JsonElement> ResponseSchema => new()
    {
        ["type"] = JsonSerializer.SerializeToElement("object"),
        ["properties"] = JsonSerializer.SerializeToElement(new
        {
            questions = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        question = new { type = "string" },
                        why_it_might_come_up = new { type = "string", description = "One sentence on why this might come up." },
                    },
                    required = new[] { "question", "why_it_might_come_up" },
                    additionalProperties = false,
                },
                description = "Likely interview questions, including ones probing gaps between the resume and the posting.",
            },
            talking_points = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        point = new { type = "string", description = "Something from the resume worth proactively highlighting." },
                        how_to_use_it = new { type = "string", description = "One sentence on when/how to bring it up." },
                    },
                    required = new[] { "point", "how_to_use_it" },
                    additionalProperties = false,
                },
                description = "Resume-grounded points worth proactively highlighting for this posting.",
            },
        }),
        ["required"] = JsonSerializer.SerializeToElement(new[] { "questions", "talking_points" }),
        ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
    };
}
