using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;

namespace CareerConnect.Api.Services;

/// <summary>
/// Rewrites a candidate's resume to fit a specific posting using structured
/// outputs. Every fact in the original resume is treated as ground truth —
/// this reorders, reframes, and tightens, but is instructed never to invent
/// employers, dates, metrics, or skills the original doesn't already state.
/// </summary>
public class ClaudeResumeTailorer : IResumeTailorer
{
    private const string SystemPrompt = """
        You are an experienced resume writer helping a candidate tailor their
        existing resume to a specific job posting.

        You will be given the candidate's current resume text and a job
        posting. Rewrite the resume so it fits this specific role better:
        emphasize and reorder content that matches what the posting asks for,
        tighten phrasing, and reframe existing bullets toward the posting's
        own language — while treating every fact already in the resume as
        ground truth you must preserve exactly.

        You may:
        - Reorder sections or bullets to lead with what matters most for this posting.
        - Reword bullets to use the posting's terminology, when the underlying fact supports it.
        - Tighten or cut content that's irrelevant to this posting.
        - Adjust an objective/summary line to speak to this specific role.

        You may not:
        - Add skills, employers, degrees, certifications, or experience not already in the original.
        - Change dates, titles, or company names.
        - Invent metrics or outcomes not already stated.

        Where a stronger bullet would benefit from a specific detail the
        original doesn't state (a number, a scale, a name), use a bracketed
        placeholder like [describe the specific outcome] rather than
        inventing one — the same convention used for suggested edits
        elsewhere in this app.

        Keep the same overall structure and formatting conventions as the
        original (bullets stay bullets, section headers stay recognizable)
        unless reordering them is the whole point of a change.

        Return the complete rewritten resume as plain text, ready to be saved
        and used as-is.
        """;

    private readonly AnthropicClient? _client;
    private readonly string _model;
    private readonly Effort _effort;

    public ClaudeResumeTailorer(IConfiguration configuration)
    {
        _model = AnthropicClientFactory.ResolveModel(configuration);
        _effort = AnthropicClientFactory.ResolveEffort(configuration);
        _client = AnthropicClientFactory.CreateClient(configuration);
    }

    public bool IsConfigured => _client is not null;

    public async Task<string> TailorAsync(
        string resumeText,
        string jobDescriptionText,
        string roleTitle,
        string companyName,
        CancellationToken cancellationToken = default)
    {
        if (_client is null)
        {
            throw new ResumeTailorException("No Anthropic API key is configured.");
        }

        var userPrompt = $"""
            Role: {roleTitle}
            Company: {companyName}

            <job_description>
            {jobDescriptionText}
            </job_description>

            <current_resume>
            {resumeText}
            </current_resume>

            Rewrite the resume to fit this posting.
            """;

        Message response;
        try
        {
            response = await _client.Messages.Create(new MessageCreateParams
            {
                Model = _model,
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
            throw new ResumeTailorException("The tailoring request to Claude failed.", ex);
        }

        if (response.StopReason == "refusal")
        {
            throw new ResumeTailorException(
                "Claude declined to tailor this resume. Check the job description for anything unexpected.");
        }

        if (response.StopReason == "max_tokens")
        {
            throw new ResumeTailorException("The rewrite was cut off before it finished. Try a shorter resume or job description.");
        }

        var json = AnthropicResponse.ExtractText(response);
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ResumeTailorException("Claude returned an empty response.");
        }

        try
        {
            var payload = JsonSerializer.Deserialize<TailorPayload>(json, AnthropicResponse.SnakeCaseJsonOptions)
                ?? throw new ResumeTailorException("Claude returned an empty rewrite.");
            return payload.TailoredResumeText;
        }
        catch (JsonException ex)
        {
            throw new ResumeTailorException("Claude returned a rewrite that could not be read.", ex);
        }
    }

    private sealed record TailorPayload(string TailoredResumeText);

    private static Dictionary<string, JsonElement> ResponseSchema => new()
    {
        ["type"] = JsonSerializer.SerializeToElement("object"),
        ["properties"] = JsonSerializer.SerializeToElement(new
        {
            tailored_resume_text = new
            {
                type = "string",
                description = "The complete rewritten resume, plain text, ready to save and use as-is.",
            },
        }),
        ["required"] = JsonSerializer.SerializeToElement(new[] { "tailored_resume_text" }),
        ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
    };
}
