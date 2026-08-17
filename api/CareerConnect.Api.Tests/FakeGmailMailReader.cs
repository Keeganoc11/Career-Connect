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
}
