using CareerConnect.Api.Services;
using Google.Apis.Calendar.v3;
using Google.Apis.Gmail.v1;

namespace CareerConnect.Api.Tests;

/// <summary>
/// Stand-in for the real OAuth service so scan orchestration can be tested
/// without a Google account. GetGmailServiceAsync intentionally throws —
/// the scanner should never call it directly, only through IGmailMailReader.
/// </summary>
public sealed class FakeGmailOAuthService : IGmailOAuthService
{
    public bool IsConfigured { get; set; } = true;

    /// <summary>Null simulates "not connected".</summary>
    public GmailConnectionInfo? Connection { get; set; } =
        new("me@example.com", DateTime.UtcNow.AddDays(-10), LastCheckedAtUtc: null, HasPendingSuggestions: false);

    public int MarkCheckedCallCount { get; private set; }

    /// <summary>Who was disconnected, in order — account deletion has to do this before the row goes.</summary>
    public List<Guid> DisconnectedUserIds { get; } = [];

    public string BuildAuthorizationUrl(string redirectUri, string state) =>
        $"https://accounts.google.com/fake?redirect_uri={redirectUri}&state={state}";

    public Task<GmailConnectionInfo> ConnectAsync(
        Guid userId, string code, string redirectUri, CancellationToken cancellationToken = default)
    {
        Connection = new GmailConnectionInfo("me@example.com", DateTime.UtcNow, LastCheckedAtUtc: null, HasPendingSuggestions: false);
        return Task.FromResult(Connection);
    }

    public Task<GmailConnectionInfo?> GetConnectionAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Connection);

    public Task DisconnectAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        DisconnectedUserIds.Add(userId);
        Connection = null;
        return Task.CompletedTask;
    }

    public Task MarkCheckedAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        MarkCheckedCallCount++;
        if (Connection is not null)
        {
            Connection = Connection with { LastCheckedAtUtc = DateTime.UtcNow };
        }
        return Task.CompletedTask;
    }

    public Task<GmailService?> GetGmailServiceAsync(Guid userId, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(
            "The scanner should go through IGmailMailReader, not call this directly.");

    /// <summary>Null throughout — calendar behaviour is tested through IInterviewCalendarSync, not here.</summary>
    public Task<CalendarService?> GetCalendarServiceAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<CalendarService?>(null);}
