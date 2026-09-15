using CareerConnect.Api.Services;

namespace CareerConnect.Api.Tests;

public sealed class FakeGmailMailReader : IGmailMailReader
{
    public List<CandidateEmail> Result { get; set; } = [];

    /// <summary>Set to throw instead of returning, to exercise the failure path.</summary>
    public Exception? ThrowOnRead { get; set; }

    public DateTime? LastAfterArgument { get; private set; }

    public Task<List<CandidateEmail>> GetRecentCandidateEmailsAsync(
        Guid userId, DateTime? after, CancellationToken cancellationToken = default)
    {
        LastAfterArgument = after;
        if (ThrowOnRead is not null)
        {
            throw ThrowOnRead;
        }
        return Task.FromResult(Result);
    }

    /// <summary>Bodies keyed by message id. Anything absent simply gets no time extracted.</summary>
    public Dictionary<string, string> Bodies { get; set; } = [];

    public Exception? ThrowOnReadBodies { get; set; }

    public List<string> LastRequestedBodyIds { get; private set; } = [];

    public Task<Dictionary<string, string>> GetBodiesAsync(
        Guid userId, IReadOnlyCollection<string> messageIds, CancellationToken cancellationToken = default)
    {
        LastRequestedBodyIds = messageIds.ToList();
        if (ThrowOnReadBodies is not null)
        {
            throw ThrowOnReadBodies;
        }

        return Task.FromResult(
            messageIds.Where(Bodies.ContainsKey).ToDictionary(id => id, id => Bodies[id]));
    }
}
