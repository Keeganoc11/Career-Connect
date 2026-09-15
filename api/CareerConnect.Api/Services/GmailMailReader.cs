using Google.Apis.Gmail.v1;

namespace CareerConnect.Api.Services;

/// <summary>
/// Searches Gmail for messages that look job-related and pulls just enough per
/// message (subject, sender, snippet) to hand to the classifier. Full bodies
/// are fetched only through <see cref="GetBodiesAsync"/>, and only for emails
/// already identified as interview invitations — the scheduled time lives in
/// the body and nowhere else. Nothing is persisted beyond the scan that
/// requested it.
/// </summary>
public class GmailMailReader(IGmailOAuthService oauth) : IGmailMailReader
{
    private const int MaxCandidates = 30;
    private const int DefaultLookbackDays = 30;

    // Fetch message details concurrently rather than one at a time, but keep
    // a modest cap so a full-size scan doesn't burst past Gmail's per-user
    // rate limit.
    private const int MaxConcurrentMessageFetches = 5;

    // Cast a reasonably wide net; the classifier does the precise filtering.
    private static readonly string[] SignalKeywords =
    [
        "interview", "next steps", "moving forward", "offer", "unfortunately",
        "regret", "not moving forward", "other candidates", "position has been filled",
        "application", "candidacy", "thank you for applying", "assessment", "screen",
    ];

    public async Task<List<CandidateEmail>> GetRecentCandidateEmailsAsync(
        Guid userId, DateTime? after, CancellationToken cancellationToken = default)
    {
        var gmail = await oauth.GetGmailServiceAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException("Gmail is not connected.");
        using var _ = gmail;

        var afterDate = after?.ToLocalTime() ?? DateTime.UtcNow.AddDays(-DefaultLookbackDays);
        var keywordQuery = string.Join(" OR ", SignalKeywords.Select(k => k.Contains(' ') ? $"\"{k}\"" : k));
        var query = $"after:{afterDate:yyyy/MM/dd} -category:promotions -category:social ({keywordQuery})";

        var listRequest = gmail.Users.Messages.List("me");
        listRequest.Q = query;
        listRequest.MaxResults = MaxCandidates;
        var listResponse = await listRequest.ExecuteAsync(cancellationToken);

        using var throttle = new SemaphoreSlim(MaxConcurrentMessageFetches);
        var fetches = (listResponse.Messages ?? []).Select(async (messageRef, index) =>
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                var getRequest = gmail.Users.Messages.Get("me", messageRef.Id);
                getRequest.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
                getRequest.MetadataHeaders = new Google.Apis.Util.Repeatable<string>(["Subject", "From"]);
                var message = await getRequest.ExecuteAsync(cancellationToken);

                var headers = message.Payload?.Headers ?? [];
                var subject = headers.FirstOrDefault(h => h.Name == "Subject")?.Value ?? "(no subject)";
                var from = headers.FirstOrDefault(h => h.Name == "From")?.Value ?? "(unknown sender)";
                var receivedAtUtc = message.InternalDate.HasValue
                    ? DateTimeOffset.FromUnixTimeMilliseconds(message.InternalDate.Value).UtcDateTime
                    : DateTime.UtcNow;

                return new CandidateEmail(index, subject, from, message.Snippet ?? "", receivedAtUtc, messageRef.Id);
            }
            finally
            {
                throttle.Release();
            }
        });

        // Task.WhenAll preserves input order regardless of completion order,
        // so CandidateEmail.Index still lines up with each email's position.
        var candidates = await Task.WhenAll(fetches);
        return candidates.ToList();
    }

    public async Task<Dictionary<string, string>> GetBodiesAsync(
        Guid userId, IReadOnlyCollection<string> messageIds, CancellationToken cancellationToken = default)
    {
        if (messageIds.Count == 0)
        {
            return [];
        }

        var gmail = await oauth.GetGmailServiceAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException("Gmail is not connected.");
        using var _ = gmail;

        using var throttle = new SemaphoreSlim(MaxConcurrentMessageFetches);
        var fetches = messageIds.Select(async id =>
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                var getRequest = gmail.Users.Messages.Get("me", id);
                getRequest.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Full;
                var message = await getRequest.ExecuteAsync(cancellationToken);
                return (Id: id, Body: ExtractPlainText(message.Payload));
            }
            catch (Exception)
            {
                // One unreadable message shouldn't cost the whole batch its
                // times; it just won't get a date extracted.
                return (Id: id, Body: (string?)null);
            }
            finally
            {
                throttle.Release();
            }
        });

        var results = await Task.WhenAll(fetches);
        return results
            .Where(r => !string.IsNullOrWhiteSpace(r.Body))
            .ToDictionary(r => r.Id, r => r.Body!);
    }

    /// <summary>
    /// Walks the MIME tree for text/plain, which real invitations always carry
    /// alongside their HTML. Preferring it avoids shipping markup to the model
    /// and keeps the extraction prompt readable.
    /// </summary>
    private static string? ExtractPlainText(Google.Apis.Gmail.v1.Data.MessagePart? part)
    {
        if (part is null)
        {
            return null;
        }

        if (part.MimeType == "text/plain" && part.Body?.Data is not null)
        {
            return DecodeBase64Url(part.Body.Data);
        }

        return part.Parts?.Select(ExtractPlainText).FirstOrDefault(text => text is not null);
    }

    private static string? DecodeBase64Url(string data)
    {
        try
        {
            var padded = data.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(padded));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
