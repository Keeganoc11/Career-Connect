using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;

namespace CareerConnect.Api.Services;

/// <summary>
/// Matches recent emails to open job applications and infers a status change
/// using structured outputs — never asked to invent a match, so an
/// unconfident classification is simply omitted rather than guessed.
/// </summary>
public class ClaudeEmailStatusClassifier : IEmailStatusClassifier
{
    private const string SystemPrompt = """
        You are helping a job seeker understand what their recent emails mean
        for their job search. You have two separate jobs to do for each email.

        JOB 1 — STATUS MATCHING: does this email clearly relate to one of the
        listed already-tracked applications, and if so, what status does it now
        imply?

        Valid statuses: Applied, PhoneScreen, Interview, Offer, Rejected, Ghosted.
        (Withdrawn is set by the candidate directly — never infer it. Preparing
        means the candidate hadn't applied yet as of the last time they looked,
        so it is a current status you will see, never one you suggest.)

        Only report a match when you're reasonably confident which application it
        belongs to — matching on company name is usually enough; use role title
        and content to disambiguate when a company has more than one open
        application. Automated "we received your application" confirmations
        don't imply any status change beyond Applied — skip those unless the
        application isn't already at Applied or further along.

        Pay particular attention to applications currently at Preparing: the
        candidate was getting ready to apply, so a confirmation email from that
        company is the evidence they went through with it. Report those as
        Applied — this is the one case where a routine confirmation is worth
        reporting rather than skipping.

        JOB 2 — NEW APPLICATION DETECTION: is this email a personal "we
        received your application" / "thank you for applying" confirmation for
        a company that is NOT already in the tracked applications list? If so,
        report it as a new application with the company name and, if stated,
        the role title.

        Be conservative here — only report emails that are clearly a direct
        confirmation of an application the candidate personally submitted.
        Never report job-alert digests, "jobs you may like" recommendations,
        recruiter cold outreach, newsletters, or marketing as new applications.
        If the role title isn't clearly stated, leave it as an empty string
        rather than guessing. If the company already appears in the tracked
        list, that email belongs under Job 1 (or nowhere) — never report it as
        new, since that would create a duplicate.

        For every status match, also say how certain it is:
        - "clear": the email states the outcome in so many words — "we've
          decided not to move forward", "we'd like to schedule a phone screen",
          "your interview is confirmed for…", "we received your application".
        - "likely": the status is implied or inferred — a vague "we'll be in
          touch", a scheduling tool link with no context, a recruiter asking
          for availability without saying what for.
        Clear matches are applied automatically (the candidate can undo them),
        so reserve "clear" for emails where nobody reading it could disagree.

        Every email should be considered for both jobs, but most emails will
        match neither — that's expected. Marketing, newsletters, and anything
        unrelated should never be reported under either job. When genuinely
        ambiguous, leave it out rather than guessing — a missed update is far
        less costly than a wrong one.
        """;

    private readonly AnthropicClient? _client;
    private readonly string _model;

    public ClaudeEmailStatusClassifier(IConfiguration configuration)
    {
        _model = AnthropicClientFactory.ResolveModel(configuration);
        _client = AnthropicClientFactory.CreateClient(configuration);
    }

    public bool IsConfigured => _client is not null;

    public async Task<EmailClassificationResult> ClassifyAsync(
        List<CandidateEmail> emails,
        List<OpenApplicationContext> openApplications,
        CancellationToken cancellationToken = default)
    {
        if (_client is null)
        {
            throw new InvalidOperationException("No Anthropic API key is configured.");
        }

        if (emails.Count == 0)
        {
            return new EmailClassificationResult([], []);
        }

        var response = await _client.Messages.Create(new MessageCreateParams
        {
            Model = _model,
            MaxTokens = 4000,
            System = SystemPrompt,
            OutputConfig = new OutputConfig
            {
                Effort = Effort.Medium,
                Format = new JsonOutputFormat { Schema = ResponseSchema },
            },
            Messages = [new() { Role = Role.User, Content = BuildPrompt(emails, openApplications) }],
        });

        if (response.StopReason == "refusal" || response.StopReason == "max_tokens")
        {
            return new EmailClassificationResult([], []);
        }

        var json = AnthropicResponse.ExtractText(response);

        if (string.IsNullOrWhiteSpace(json))
        {
            return new EmailClassificationResult([], []);
        }

        var payload = JsonSerializer.Deserialize<ClassificationPayload>(json, AnthropicResponse.SnakeCaseJsonOptions);
        return new EmailClassificationResult(payload?.Matches ?? [], payload?.NewApplications ?? []);
    }

    private static string BuildPrompt(List<CandidateEmail> emails, List<OpenApplicationContext> applications)
    {
        var appLines = applications.Count == 0
            ? "(none currently tracked)"
            : string.Join("\n", applications.Select(a =>
                $"[{a.Index}] {a.CompanyName} — {a.RoleTitle} (current status: {a.CurrentStatus})"));

        var emailLines = string.Join("\n\n", emails.Select(e =>
            $"[{e.Index}] From: {e.From}\nSubject: {e.Subject}\nReceived: {e.ReceivedAtUtc:yyyy-MM-dd}\nPreview: {e.Snippet}"));

        return $"""
            Tracked applications:
            {appLines}

            Recent emails:
            {emailLines}
            """;
    }

    private sealed record ClassificationPayload(
        List<EmailClassificationMatch> Matches,
        List<EmailNewApplicationMatch> NewApplications);

    private static Dictionary<string, JsonElement> ResponseSchema => new()
    {
        ["type"] = JsonSerializer.SerializeToElement("object"),
        ["properties"] = JsonSerializer.SerializeToElement(new
        {
            matches = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        email_index = new { type = "integer", description = "The [N] index of the matched email." },
                        application_index = new { type = "integer", description = "The [N] index of the tracked application it relates to." },
                        suggested_status = new
                        {
                            type = "string",
                            @enum = new[] { "Applied", "PhoneScreen", "Interview", "Offer", "Rejected", "Ghosted" },
                            description = "The status this email implies for that application.",
                        },
                        reasoning = new
                        {
                            type = "string",
                            description = "One sentence explaining the match, referencing what the email actually says.",
                        },
                        certainty = new
                        {
                            type = "string",
                            @enum = new[] { "clear", "likely" },
                            description = "\"clear\" only when the email states the outcome outright; otherwise \"likely\".",
                        },
                    },
                    required = new[] { "email_index", "application_index", "suggested_status", "reasoning", "certainty" },
                    additionalProperties = false,
                },
                description = "Only confident status matches against tracked applications — omit anything ambiguous or unrelated.",
            },
            new_applications = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        email_index = new { type = "integer", description = "The [N] index of the confirmation email." },
                        company_name = new { type = "string", description = "The company name, as stated in the email." },
                        role_title = new { type = "string", description = "The role title, if clearly stated; empty string if not." },
                        reasoning = new
                        {
                            type = "string",
                            description = "One sentence explaining why this looks like a new application confirmation.",
                        },
                    },
                    required = new[] { "email_index", "company_name", "role_title", "reasoning" },
                    additionalProperties = false,
                },
                description = "Only confident, personal application confirmations for companies NOT already tracked — never job alerts, recommendations, or marketing.",
            },
        }),
        ["required"] = JsonSerializer.SerializeToElement(new[] { "matches", "new_applications" }),
        ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
    };
}
