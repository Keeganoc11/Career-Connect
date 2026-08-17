using Google.Apis.Gmail.v1;

namespace CareerConnect.Api.Services;

/// <summary>
/// Searches Gmail for messages that look job-related and pulls just enough
/// per message (subject, sender, snippet) to hand to the classifier — never
/// the full body, and nothing is persisted beyond the scan that requested it.
/// </summary>
public class GmailMailReader(IGmailOAuthService oauth) : IGmailMailReader
{
    private const int MaxCandidates = 30;
    private const int DefaultLookbackDays = 30;

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

        var candidates = new List<CandidateEmail>();
        var index = 0;
        foreach (var messageRef in listResponse.Messages ?? [])
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

            candidates.Add(new CandidateEmail(index++, subject, from, message.Snippet ?? "", receivedAtUtc));
        }

        return candidates;
    }
}
