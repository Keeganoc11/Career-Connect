using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;

namespace CareerConnect.Api.Services;

public class ClaudeInterviewDetailsExtractor : IInterviewDetailsExtractor
{
    /// <summary>
    /// Bodies are long and mostly boilerplate; the scheduling line is near the
    /// top in practice. Truncating keeps a scan's token cost bounded.
    /// </summary>
    private const int MaxBodyCharacters = 4000;

    private const string SystemPrompt =
        """
        You extract interview scheduling details from emails a job applicant has received.

        For each email, decide whether it states a SPECIFIC date and time for an interview,
        phone screen, or assessment that the candidate is expected to attend.

        RETURNING THE TIME
        - Only return a time when the email actually states one. An email that asks the
          candidate to pick a slot, offers a range of options, or says "we'll be in touch
          to schedule" has NOT pinned down a time — return null for it.
        - Return an ISO-8601 timestamp WITH an explicit UTC offset, e.g. 2026-09-14T14:00:00-04:00.
        - Take the offset from what the email says. If it names a timezone ("2pm ET",
          "14:00 CEST"), convert that to an offset. If it gives a time with no timezone
          at all, return null — a guessed offset is worse than no time, because it puts
          a wrong appointment on someone's calendar.
        - Each email's received date is given. Use it to resolve relative dates like
          "this Thursday" or "tomorrow". If that still leaves the date ambiguous, return null.
        - Never invent or infer a time that is not stated. Null is the correct, safe answer.

        KIND
        Classify as one of: PhoneScreen, Technical, Onsite, Final, Other.
        - PhoneScreen: an initial recruiter or screening call.
        - Technical: a coding, systems design, or technical assessment round.
        - Onsite: an in-person or full-day/multi-round loop.
        - Final: an explicitly final round, or with senior leadership.
        - Other: interview-related but none of the above fit.

        Return one entry per email you were given, in the same order, using the same index.
        """;

    private readonly AnthropicClient? _client;
    private readonly string _model;
    private readonly Effort _effort;
    private readonly ILogger<ClaudeInterviewDetailsExtractor> _logger;

    public ClaudeInterviewDetailsExtractor(
        IConfiguration configuration, ILogger<ClaudeInterviewDetailsExtractor> logger)
    {
        _model = AnthropicClientFactory.ResolveModel(configuration);
        _client = AnthropicClientFactory.CreateClient(configuration);
        _effort = AnthropicClientFactory.ResolveEffort(configuration);
        _logger = logger;
    }

    public bool IsConfigured => _client is not null;

    public async Task<List<ExtractedInterviewDetails>> ExtractAsync(
        List<InterviewEmailContext> emails, CancellationToken cancellationToken = default)
    {
        if (_client is null || emails.Count == 0)
        {
            return [];
        }

        try
        {
            var response = await _client.Messages.Create(new MessageCreateParams
            {
                Model = _model,
                MaxTokens = 2000,
                System = SystemPrompt,
                OutputConfig = new OutputConfig
                {
                    Effort = _effort,
                    Format = new JsonOutputFormat { Schema = ResponseSchema },
                },
                Messages = [new() { Role = Role.User, Content = BuildPrompt(emails) }],
            }, cancellationToken);

            if (response.StopReason == "refusal" || response.StopReason == "max_tokens")
            {
                return [];
            }

            var json = AnthropicResponse.ExtractText(response);
            if (string.IsNullOrWhiteSpace(json))
            {
                return [];
            }

            var payload = JsonSerializer.Deserialize<ExtractionPayload>(json, AnthropicResponse.SnakeCaseJsonOptions);
            return (payload?.Interviews ?? [])
                .Select(i => new ExtractedInterviewDetails(i.EmailIndex, ParseTimestamp(i.ScheduledAt), i.Kind))
                .ToList();
        }
        catch (Exception ex)
        {
            // A missing date is a degraded scan, not a failed one — the status
            // suggestion is still worth showing, so this never propagates.
            _logger.LogWarning(ex, "Couldn't extract interview times from this scan's emails.");
            return [];
        }
    }

    /// <summary>
    /// Requires a real offset. <see cref="DateTimeStyles.AssumeUniversal"/> would
    /// quietly turn an offsetless string into UTC, which is exactly the wrong
    /// appointment — the prompt asks for null instead, and this enforces it.
    /// </summary>
    private static DateTimeOffset? ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }

    private static string BuildPrompt(List<InterviewEmailContext> emails)
    {
        var blocks = emails.Select(e =>
        {
            var body = e.Body.Length > MaxBodyCharacters ? e.Body[..MaxBodyCharacters] : e.Body;
            return $"""
                [{e.Index}] {e.CompanyName} — {e.RoleTitle}
                Subject: {e.Subject}
                Received: {e.ReceivedAtUtc:yyyy-MM-dd HH:mm} UTC
                Body:
                {body}
                """;
        });

        return $"""
            Extract interview scheduling details from these {emails.Count} emails.

            {string.Join("\n\n---\n\n", blocks)}
            """;
    }

    private sealed record ExtractedRow(int EmailIndex, string? ScheduledAt, string Kind);

    private sealed record ExtractionPayload(List<ExtractedRow> Interviews);

    private static Dictionary<string, JsonElement> ResponseSchema => new()
    {
        ["type"] = JsonSerializer.SerializeToElement("object"),
        ["properties"] = JsonSerializer.SerializeToElement(new
        {
            interviews = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        email_index = new { type = "integer", description = "The [N] index of the email." },
                        scheduled_at = new
                        {
                            type = new[] { "string", "null" },
                            description =
                                "ISO-8601 with an explicit offset, e.g. 2026-09-14T14:00:00-04:00. " +
                                "Null unless the email states a specific date AND time AND timezone.",
                        },
                        kind = new
                        {
                            type = "string",
                            @enum = new[] { "PhoneScreen", "Technical", "Onsite", "Final", "Other" },
                            description = "What kind of interview this is.",
                        },
                    },
                    required = new[] { "email_index", "scheduled_at", "kind" },
                    additionalProperties = false,
                },
            },
        }),
        ["required"] = JsonSerializer.SerializeToElement(new[] { "interviews" }),
        ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
    };
}
