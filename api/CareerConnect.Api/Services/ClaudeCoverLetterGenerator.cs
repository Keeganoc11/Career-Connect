using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;

namespace CareerConnect.Api.Services;

/// <summary>
/// Writes a complete cover letter using structured outputs. Every claim is
/// grounded in the candidate's actual resume — instructed never to invent
/// employers, skills, or achievements the resume doesn't already state.
/// </summary>
public class ClaudeCoverLetterGenerator : ICoverLetterGenerator
{
    private const string SystemPrompt = """
        You are helping a job seeker write a cover letter for a specific role,
        based on their actual resume.

        Ground every claim in what the resume actually states — never invent
        employers, skills, metrics, or achievements not already present.
        Where the posting's requirements aren't evidenced in the resume,
        don't claim them outright; speak honestly about transferable
        experience instead, or leave it out.

        Write a complete, ready-to-send letter: 3-4 paragraphs, professional
        but not stiff, specific to this role and company (reference what the
        posting actually asks for, not generic enthusiasm). First person. Use
        "Dear Hiring Team," as the greeting and a generic sign-off (the
        candidate's name isn't provided to you) — no bracketed placeholders
        for those. Bracketed placeholders like [describe the specific
        outcome] are for the rare case where a stronger sentence would need a
        factual detail the resume doesn't supply.

        Return the complete letter as plain text, ready to send as-is.
        """;

    private readonly AnthropicClient? _client;
    private readonly string _model;
    private readonly Effort _effort;

    public ClaudeCoverLetterGenerator(IConfiguration configuration)
    {
        _model = AnthropicClientFactory.ResolveModel(configuration);
        _effort = AnthropicClientFactory.ResolveEffort(configuration);
        _client = AnthropicClientFactory.CreateClient(configuration);
    }

    public bool IsConfigured => _client is not null;

    public async Task<string> GenerateAsync(
        string resumeText,
        string jobDescriptionText,
        string roleTitle,
        string companyName,
        CancellationToken cancellationToken = default)
    {
        if (_client is null)
        {
            throw new CoverLetterGenerationException("No Anthropic API key is configured.");
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

            Write a cover letter for this application.
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
            throw new CoverLetterGenerationException("The cover letter request to Claude failed.", ex);
        }

        if (response.StopReason == "refusal")
        {
            throw new CoverLetterGenerationException(
                "Claude declined to write this cover letter. Check the job description for anything unexpected.");
        }

        if (response.StopReason == "max_tokens")
        {
            throw new CoverLetterGenerationException("The letter was cut off before it finished. Try a shorter job description.");
        }

        var json = AnthropicResponse.ExtractText(response);
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new CoverLetterGenerationException("Claude returned an empty response.");
        }

        try
        {
            var payload = JsonSerializer.Deserialize<CoverLetterPayload>(json, AnthropicResponse.SnakeCaseJsonOptions)
                ?? throw new CoverLetterGenerationException("Claude returned an empty letter.");
            return payload.CoverLetterText;
        }
        catch (JsonException ex)
        {
            throw new CoverLetterGenerationException("Claude returned a letter that could not be read.", ex);
        }
    }

    private sealed record CoverLetterPayload(string CoverLetterText);

    private static Dictionary<string, JsonElement> ResponseSchema => new()
    {
        ["type"] = JsonSerializer.SerializeToElement("object"),
        ["properties"] = JsonSerializer.SerializeToElement(new
        {
            cover_letter_text = new
            {
                type = "string",
                description = "The complete cover letter, plain text, ready to send as-is.",
            },
        }),
        ["required"] = JsonSerializer.SerializeToElement(new[] { "cover_letter_text" }),
        ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
    };
}
