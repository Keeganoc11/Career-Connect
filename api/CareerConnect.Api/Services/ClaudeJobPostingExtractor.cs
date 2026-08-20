using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;

namespace CareerConnect.Api.Services;

/// <summary>
/// Turns a scraped page's raw text into structured job-posting fields using
/// structured outputs — the page text is often noisy (nav debris, cookie
/// banners, unrelated sidebar content the HTML stripping missed), so this is
/// asked to explicitly say when a page isn't a job posting rather than
/// fabricate a plausible-looking one.
/// </summary>
public class ClaudeJobPostingExtractor : IJobPostingExtractor
{
    private const string SystemPrompt = """
        You are extracting structured data from the text of a web page a job
        seeker pasted in, hoping it's a job posting.

        First decide: is this actually a single job posting (not a search
        results page, a company careers homepage listing many roles, a login
        wall, an expired/removed posting, or an unrelated page)? Be honest
        about this — if you can't find a specific role with real
        responsibilities/requirements, set is_job_posting to false and leave
        the other fields empty rather than guessing from fragments.

        If it is a job posting, extract:
        - company_name: the hiring company's name as stated.
        - role_title: the job title as stated.
        - job_description: the substantive posting content — responsibilities,
          requirements, qualifications, and a brief role summary if present.
          Reformat it as clean, readable text (plain paragraphs and "- " bullet
          lists), stripped of navigation text, cookie banners, "Apply now"
          buttons, unrelated related-postings lists, and other page furniture
          that survived the HTML-to-text conversion. Do not summarize or
          shorten the substantive content — reproduce it faithfully, just
          cleaned up.
        """;

    private readonly AnthropicClient? _client;
    private readonly string _model;

    public ClaudeJobPostingExtractor(IConfiguration configuration)
    {
        _model = AnthropicClientFactory.ResolveModel(configuration);
        _client = AnthropicClientFactory.CreateClient(configuration);
    }

    public bool IsConfigured => _client is not null;

    public async Task<JobPostingExtraction> ExtractAsync(string pageText, CancellationToken cancellationToken = default)
    {
        if (_client is null)
        {
            throw new JobPostingExtractionException("No Anthropic API key is configured.");
        }

        Message response;
        try
        {
            response = await _client.Messages.Create(new MessageCreateParams
            {
                Model = _model,
                // Faithfully reproducing a full posting can run long, and on
                // thinking-enabled models MaxTokens caps thinking plus the
                // response together — a tight budget truncates either way.
                MaxTokens = 8000,
                System = SystemPrompt,
                OutputConfig = new OutputConfig
                {
                    Effort = Effort.Medium,
                    Format = new JsonOutputFormat { Schema = ResponseSchema },
                },
                Messages = [new() { Role = Role.User, Content = $"<page_text>\n{pageText}\n</page_text>" }],
            });
        }
        catch (Exception ex)
        {
            throw new JobPostingExtractionException("The extraction request to Claude failed.", ex);
        }

        if (response.StopReason == "refusal")
        {
            throw new JobPostingExtractionException(
                "Claude declined to read this page. Try pasting the job description directly instead.");
        }

        if (response.StopReason == "max_tokens")
        {
            throw new JobPostingExtractionException("The posting was too long to extract in one pass.");
        }

        var json = AnthropicResponse.ExtractText(response);
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new JobPostingExtractionException("Claude returned an empty response.");
        }

        try
        {
            var payload = JsonSerializer.Deserialize<ExtractionPayload>(json, AnthropicResponse.SnakeCaseJsonOptions)
                ?? throw new JobPostingExtractionException("Claude returned an empty extraction.");

            return new JobPostingExtraction(
                payload.IsJobPosting, payload.CompanyName, payload.RoleTitle, payload.JobDescription);
        }
        catch (JsonException ex)
        {
            throw new JobPostingExtractionException("Claude returned an extraction that could not be read.", ex);
        }
    }

    private sealed record ExtractionPayload(
        bool IsJobPosting, string CompanyName, string RoleTitle, string JobDescription);

    private static Dictionary<string, JsonElement> ResponseSchema => new()
    {
        ["type"] = JsonSerializer.SerializeToElement("object"),
        ["properties"] = JsonSerializer.SerializeToElement(new
        {
            is_job_posting = new
            {
                type = "boolean",
                description = "Whether this page is a single, specific job posting with real content to extract.",
            },
            company_name = new { type = "string", description = "The hiring company's name, or empty string if not a job posting." },
            role_title = new { type = "string", description = "The job title, or empty string if not a job posting." },
            job_description = new
            {
                type = "string",
                description = "Cleaned, faithfully reproduced posting content, or empty string if not a job posting.",
            },
        }),
        ["required"] = JsonSerializer.SerializeToElement(
            new[] { "is_job_posting", "company_name", "role_title", "job_description" }),
        ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
    };
}
