using System.Threading.Channels;

namespace CareerConnect.Api.Services;

public interface IPrepRunQueue
{
    void Enqueue(Guid prepRunId);
    IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Hands started prep runs to the background worker. Unbounded because the
/// producer is one authenticated user clicking a button, and dropping a run the
/// user is already watching would be worse than a queue that grows.
/// </summary>
public class PrepRunQueue : IPrepRunQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(Guid prepRunId) => _channel.Writer.TryWrite(prepRunId);

    public IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
