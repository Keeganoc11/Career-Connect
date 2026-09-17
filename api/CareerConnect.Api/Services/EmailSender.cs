using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace CareerConnect.Api.Services;

public record EmailMessage(string To, string Subject, string HtmlBody, string TextBody);

public interface IEmailSender
{
    /// <summary>False when no provider is configured — callers say so rather than pretending mail went out.</summary>
    bool IsConfigured { get; }

    /// <summary>Returns false when sending failed. Never throws: a dead mail provider must not 500 a request.</summary>
    Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

/// <summary>
/// Transactional mail through Resend's HTTP API.
///
/// A plain POST rather than a client library: it's one endpoint with four
/// fields, and the SDK would be a dependency to track for no benefit.
/// </summary>
public class ResendEmailSender(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<ResendEmailSender> logger) : IEmailSender
{
    private readonly string? _apiKey = configuration["Email:ApiKey"];

    // Must be an address on a domain verified with the provider, or every
    // send is rejected.
    private readonly string _from = configuration["Email:From"] ?? "Career Connect <onboarding@resend.dev>";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public async Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            logger.LogWarning("Email:ApiKey is not configured; no mail was sent to {To}.", message.To);
            return false;
        }

        var client = httpClientFactory.CreateClient(nameof(ResendEmailSender));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        try
        {
            var response = await client.PostAsJsonAsync("https://api.resend.com/emails", new
            {
                from = _from,
                to = new[] { message.To },
                subject = message.Subject,
                html = message.HtmlBody,
                text = message.TextBody,
            }, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            // The body carries the actual reason — an unverified domain, a
            // malformed From — and without it this is undiagnosable.
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogError(
                "Sending mail failed with {Status}: {Body}", (int)response.StatusCode, body);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Sending mail to {To} threw.", message.To);
            return false;
        }
    }
}

/// <summary>
/// Writes the mail to the log instead of sending it.
///
/// This is what makes local development and the tests work with no provider
/// and no network: a reset link printed in the API's terminal is enough to
/// walk the whole flow. Registered only when no API key is configured, so it
/// can never silently replace real sending in production.
/// </summary>
public class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public bool IsConfigured => true;

    public Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Email not sent (no provider configured). To: {To}\nSubject: {Subject}\n{Body}",
            message.To,
            message.Subject,
            message.TextBody);

        return Task.FromResult(true);
    }
}
