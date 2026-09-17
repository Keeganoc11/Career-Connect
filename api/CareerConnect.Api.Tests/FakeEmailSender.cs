using CareerConnect.Api.Services;

namespace CareerConnect.Api.Tests;

public sealed class FakeEmailSender : IEmailSender
{
    public bool IsConfigured { get; set; } = true;

    public bool NextSendFails { get; set; }

    public List<EmailMessage> Sent { get; } = [];

    public EmailMessage? Last => Sent.LastOrDefault();

    /// <summary>The token out of the last link, which is the only place it ever exists.</summary>
    public string? LastToken
    {
        get
        {
            var body = Last?.TextBody;
            if (body is null) return null;

            var marker = "token=";
            var start = body.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return null;

            var value = body[(start + marker.Length)..];
            var end = value.IndexOfAny([' ', '\n', '\r']);
            return Uri.UnescapeDataString(end < 0 ? value : value[..end]);
        }
    }

    public Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        Sent.Add(message);
        return Task.FromResult(!NextSendFails);
    }
}
